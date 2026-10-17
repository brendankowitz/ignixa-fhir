// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Reading;

namespace Ignixa.CliOnFhir.Indexing;

/// <summary>
/// Resolves conditional references (<c>Practitioner?identifier=http://hl7.org/fhir/sid/us-npi|9999885798</c>, as Synthea writes
/// them in transaction Bundles) to the <c>Type/id</c> of the one resource of that type in the folder with that identifier,
/// as a server does when it processes the transaction (R4 http.html#trules, "Conditional References": exactly one match
/// resolves; none or several fail the transaction). Only <c>identifier=[system|]value</c> criteria are evaluated.
/// </summary>
/// <remarks>
/// <para>
/// Each type's identifiers are read from the resources' own JSON (their <c>identifier</c> element), not from a search index,
/// so resolving never waits on, or recurses into, another type's index build. A type's lookup is built on the first
/// conditional reference to it, once per session, reading only the files that hold that type. Safe for concurrent use.
/// </para>
/// <para>
/// The lookup models conditional creates: a Bundle entry whose <c>request.ifNoneExist</c> is an identifier criterion that a
/// resource earlier in catalog order already satisfies is not created by a server, so it does not count as a match (two
/// Synthea runs in one folder POST the same practitioner, each with <c>ifNoneExist</c> on its NPI; the first wins).
/// </para>
/// <para>
/// A file or resource of the type that cannot be read (removed, unreadable, or changed since it was catalogued) makes every
/// lookup of that type unknown, reported as <see cref="ConditionalReferenceFailure.TargetUnreadable"/>: it might hold the
/// identifier. The failure is recorded in the lookup, never thrown, so it cannot break an index build or a read.
/// </para>
/// </remarks>
internal sealed partial class ConditionalReferenceResolver(FolderCatalog catalog)
{
    private const string IdentifierParameter = "identifier=";

    private readonly ConcurrentDictionary<string, Lazy<IdentifierLookup>> _lookups = new(StringComparer.Ordinal);

    /// <summary>Whether <paramref name="reference"/> is a conditional reference: a resource type name, <c>?</c>, criteria.</summary>
    public static bool IsConditional(string reference) => ConditionalShape().IsMatch(reference);

    /// <summary>The <c>Type/id</c> <paramref name="reference"/> (a conditional reference) names, or why it names none.</summary>
    public bool TryResolve(string reference, [NotNullWhen(true)] out string? target, [NotNullWhen(false)] out ConditionalReferenceFailure? failure)
    {
        target = null;
        failure = null;
        int question = reference.IndexOf('?', StringComparison.Ordinal);
        string resourceType = reference[..question];
        if (Criteria(reference[(question + 1)..]) is not { } identifier)
        {
            failure = ConditionalReferenceFailure.UnsupportedParameter;
            return false;
        }

        IdentifierLookup lookup = _lookups
            .GetOrAdd(resourceType, type => new Lazy<IdentifierLookup>(() => IdentifierLookup.Build(catalog, type), LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;
        IReadOnlyCollection<string> ids = lookup.Find(identifier.System, identifier.Value);
        if (lookup.HasUnreadable)
        {
            failure = ConditionalReferenceFailure.TargetUnreadable;
        }
        else if (ids.Count == 0)
        {
            failure = ConditionalReferenceFailure.NoMatch;
        }
        else if (ids.Count > 1)
        {
            failure = ConditionalReferenceFailure.Ambiguous;
        }
        else
        {
            target = $"{resourceType}/{ids.First()}";
            return true;
        }

        return false;
    }

    /// <summary>
    /// The identifier a single <c>identifier=</c> criterion asks for: <c>system|value</c>, <c>|value</c> (no system, an empty
    /// <c>System</c>) or <c>value</c> (any system, a null <c>System</c>); null for anything else, including several criteria,
    /// a modifier, a comma (or) and a FHIR escape (<c>\</c>), which are not evaluated.
    /// </summary>
    private static (string? System, string Value)? Criteria(string criteria)
    {
        if (!criteria.StartsWith(IdentifierParameter, StringComparison.Ordinal) || criteria.Contains('&', StringComparison.Ordinal))
        {
            return null;
        }

        string token = Uri.UnescapeDataString(criteria[IdentifierParameter.Length..]);
        if (token.Contains(',', StringComparison.Ordinal) || token.Contains('\\', StringComparison.Ordinal))
        {
            return null;
        }

        int bar = token.IndexOf('|', StringComparison.Ordinal);
        (string? system, string value) = bar < 0 ? (null, token) : (token[..bar], token[(bar + 1)..]);
        return value.Length == 0 ? null : (system, value);
    }

    [GeneratedRegex("^[A-Z][A-Za-z]{1,63}\\?.")]
    private static partial Regex ConditionalShape();

    /// <summary>
    /// One type's created resources by identifier: by <c>(system, value)</c>, a missing system as empty, and by value alone;
    /// and whether any resource of the type could not be read.
    /// </summary>
    private sealed class IdentifierLookup
    {
        private static readonly byte[] IfNoneExistMarker = Encoding.UTF8.GetBytes("\"ifNoneExist\"");

        private readonly Dictionary<(string System, string Value), List<string>> _bySystemAndValue = [];
        private readonly Dictionary<string, List<string>> _byValue = new(StringComparer.Ordinal);

        public bool HasUnreadable { get; private set; }

        public static IdentifierLookup Build(FolderCatalog catalog, string resourceType)
        {
            var lookup = new IdentifierLookup();
            CatalogEntry[] entries = [.. catalog.Entries.Where(entry => string.Equals(entry.ResourceType, resourceType, StringComparison.Ordinal))];
            Dictionary<string, BundleFile?> files = new(StringComparer.Ordinal);
            foreach (string path in entries.Select(entry => entry.FilePath).Distinct(StringComparer.Ordinal))
            {
                files[path] = BundleFile.Read(Path.Combine(catalog.RootPath, path));
            }

            // Catalog order, across files: an ifNoneExist is evaluated against the resources created before it.
            foreach (CatalogEntry entry in entries)
            {
                if (files[entry.FilePath] is not { } file || Identifiers(file.Bytes, entry) is not { } identifiers)
                {
                    lookup.HasUnreadable = true;
                    continue;
                }

                if (!lookup.NotCreated(file.IfNoneExist(entry)))
                {
                    lookup.Add(identifiers, entry.Id);
                }
            }

            return lookup;
        }

        public IReadOnlyCollection<string> Find(string? system, string value) =>
            (system is null ? _byValue.GetValueOrDefault(value) : _bySystemAndValue.GetValueOrDefault((system, value))) ?? [];

        /// <summary>The resource's identifiers, or null when its catalogued range no longer holds it or is not valid JSON.</summary>
        private static List<(string System, string Value)>? Identifiers(byte[] bytes, CatalogEntry entry)
        {
            if (ResourceReader.Slice(bytes, entry).Json is not { } json)
            {
                return null;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                return null;
            }

            using (document)
            {
                List<(string System, string Value)> identifiers = [];
                if (document.RootElement.TryGetProperty("identifier", out JsonElement identifier))
                {
                    IEnumerable<JsonElement> items = identifier.ValueKind == JsonValueKind.Array ? identifier.EnumerateArray() : [identifier];
                    foreach (JsonElement item in items.Where(item => item.ValueKind == JsonValueKind.Object))
                    {
                        if (StringMember(item, "value") is { Length: > 0 } value)
                        {
                            identifiers.Add((StringMember(item, "system") ?? string.Empty, value));
                        }
                    }
                }

                return identifiers;
            }
        }

        /// <summary>Whether an entry with this <c>ifNoneExist</c> was not created: an identifier criterion a resource added before it satisfies.</summary>
        private bool NotCreated(string? ifNoneExist) =>
            ifNoneExist is not null && Criteria(ifNoneExist) is { } existing && Find(existing.System, existing.Value).Count > 0;

        private void Add(List<(string System, string Value)> identifiers, string id)
        {
            foreach ((string system, string value) in identifiers)
            {
                Add(_bySystemAndValue, (system, value), id);
                Add(_byValue, value, id);
            }
        }

        private static string? StringMember(JsonElement item, string name) =>
            item.TryGetProperty(name, out JsonElement member) && member.ValueKind == JsonValueKind.String ? member.GetString() : null;

        private static void Add<TKey>(Dictionary<TKey, List<string>> map, TKey key, string id)
            where TKey : notnull
        {
            if (!map.TryGetValue(key, out List<string>? ids))
            {
                ids = [];
                map.Add(key, ids);
            }

            if (!ids.Contains(id, StringComparer.Ordinal))
            {
                ids.Add(id);
            }
        }

        /// <summary>A file's bytes and, when it is a Bundle with any, its entries' <c>request.ifNoneExist</c>.</summary>
        private sealed class BundleFile(byte[] bytes, Dictionary<string, string> byFullUrl, Dictionary<(string Type, string Id), string> byResource)
        {
            public byte[] Bytes => bytes;

            /// <summary>The file, or null when it cannot be read or no longer parses as the Bundle it was catalogued from.</summary>
            public static BundleFile? Read(string path)
            {
                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return null;
                }

                Dictionary<string, string> byFullUrl = new(StringComparer.Ordinal);
                Dictionary<(string, string), string> byResource = [];
                if (bytes.AsSpan().IndexOf(IfNoneExistMarker) < 0)
                {
                    return new BundleFile(bytes, byFullUrl, byResource);
                }

                try
                {
                    using JsonDocument document = JsonDocument.Parse(bytes);
                    if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("entry", out JsonElement bundleEntries) && bundleEntries.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement bundleEntry in bundleEntries.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object))
                        {
                            AddIfNoneExist(bundleEntry, byFullUrl, byResource);
                        }
                    }
                }
                catch (JsonException)
                {
                    return null;
                }

                return new BundleFile(bytes, byFullUrl, byResource);
            }

            public string? IfNoneExist(CatalogEntry entry) =>
                entry.FullUrl is { } fullUrl
                    ? byFullUrl.GetValueOrDefault(fullUrl)
                    : entry.HasSyntheticId ? null : byResource.GetValueOrDefault((entry.ResourceType, entry.Id));

            private static void AddIfNoneExist(JsonElement bundleEntry, Dictionary<string, string> byFullUrl, Dictionary<(string, string), string> byResource)
            {
                if (!bundleEntry.TryGetProperty("request", out JsonElement request) || request.ValueKind != JsonValueKind.Object
                    || StringMember(request, "ifNoneExist") is not { } ifNoneExist)
                {
                    return;
                }

                if (StringMember(bundleEntry, "fullUrl") is { } fullUrl)
                {
                    byFullUrl.TryAdd(fullUrl, ifNoneExist);
                }
                else if (bundleEntry.TryGetProperty("resource", out JsonElement resource) && resource.ValueKind == JsonValueKind.Object
                    && StringMember(resource, "resourceType") is { } type && StringMember(resource, "id") is { } id)
                {
                    byResource.TryAdd((type, id), ifNoneExist);
                }
            }
        }
    }
}

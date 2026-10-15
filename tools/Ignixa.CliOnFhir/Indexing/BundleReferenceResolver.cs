// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.CliOnFhir.Indexing;

/// <summary>
/// Rewrites references to Bundle entries by <c>fullUrl</c> (a <c>urn:uuid:</c>/<c>urn:oid:</c>, or an absolute URL) into the
/// relative <c>Type/id</c> of the catalogued resource, in the tool's own parse of a resource just before it is indexed, so
/// every search parameter sees the reference the server would store after processing the Bundle as a transaction. The
/// file, the shared indexer and the server's rows are unchanged.
/// </summary>
/// <remarks>
/// <para>
/// The rewrite happens on the reference text rather than on the extracted index entries because some entries only exist
/// once the reference resolves: a parameter whose expression filters with <c>resolve()</c> (R4 <c>Observation-patient</c>,
/// <c>Observation.subject.where(resolve() is Patient)</c>) indexes nothing for a <c>urn:</c>, which has no type, and a
/// parameter with a single target types it as <c>Encounter/urn:uuid:...</c>. The server rewrites a transaction's
/// references the same way before it indexes them (<c>src/Application/Ignixa.Application/Features/Bundle/BundleReferencePreProcessor.cs</c>).
/// </para>
/// <para>
/// A reference is looked up among the fullUrls of its own Bundle first, the scope FHIR resolves Bundle references in, and
/// otherwise among the whole folder's, so a resource outside a Bundle can name an entry too. A fullUrl that names two
/// different resources in a scope resolves nothing there (an ambiguous Bundle-local fullUrl does not fall back to the
/// folder), and an absolute reference no fullUrl matches is an ordinary reference to another server.
/// </para>
/// <para>
/// A <c>urn:</c> reference left unresolved is returned so the type's index can report it as <see cref="UnresolvedCode"/>;
/// that is the only report of such references, which <c>references-unresolved</c> does not count again. The resolution
/// depends on every Bundle in the folder, which the index cache accounts for: its manifest lists every catalogued file,
/// so any change invalidates every cached index. Immutable after construction, so safe for concurrent use.
/// </para>
/// </remarks>
internal sealed class BundleReferenceResolver
{
    /// <summary>The diagnostic code for a type's <c>urn:</c> references that name no catalogued Bundle entry.</summary>
    public const string UnresolvedCode = "unresolved-bundle-reference";

    /// <summary>Most unresolved references named in a type's diagnostic.</summary>
    public const int SampleLimit = 5;

    private const string UrnScheme = "urn:";

    private readonly IReferenceSearchValueParser _parser;
    private readonly Dictionary<(string FilePath, string Key), string?> _byBundle = [];
    private readonly Dictionary<string, string?> _byFolder = new(StringComparer.Ordinal);

    /// <param name="fullUrls">The catalog's Bundle entry fullUrls.</param>
    /// <param name="parser">The parser the index is built with, so a fullUrl and a reference to it are read alike.</param>
    public BundleReferenceResolver(IReadOnlyList<BundleFullUrl> fullUrls, IReferenceSearchValueParser parser)
    {
        ArgumentNullException.ThrowIfNull(fullUrls);
        ArgumentNullException.ThrowIfNull(parser);

        _parser = parser;
        foreach (BundleFullUrl fullUrl in fullUrls)
        {
            // A relative fullUrl is not one a reference could name differently from the resource's own Type/id.
            if (Key(fullUrl.FullUrl) is not { } key)
            {
                continue;
            }

            string target = $"{fullUrl.ResourceType}/{fullUrl.Id}";
            Add(_byBundle, (fullUrl.FilePath, key), target);
            Add(_byFolder, key, target);
        }
    }

    /// <summary>Whether an indexed <paramref name="reference"/> is a <c>urn:</c>, which only a Bundle entry's fullUrl can name.</summary>
    /// <remarks>The indexer types a <c>urn:</c> under a single-target parameter, so the id, not the type, tells.</remarks>
    public static bool IsUrn(ReferenceSearchValue reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return reference.BaseUri is null
            ? reference.ResourceId.StartsWith(UrnScheme, StringComparison.Ordinal)
            : reference.ToString().StartsWith(UrnScheme, StringComparison.Ordinal);
    }

    /// <summary>
    /// Replaces, in place, every <c>reference</c> in <paramref name="resource"/> that names a catalogued Bundle entry with that
    /// entry's relative <c>Type/id</c>, and returns the distinct <c>urn:</c> references that name none, in document order.
    /// </summary>
    /// <param name="entry">Where the resource was catalogued; its Bundle (its file) is searched first.</param>
    /// <param name="resource">The tool's own parse of the resource, before it is converted for indexing.</param>
    public IReadOnlyList<string> Rewrite(CatalogEntry entry, JsonObject resource)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(resource);

        List<string> unresolved = [];
        Rewrite(resource, entry.FilePath, unresolved);
        return [.. unresolved.Distinct(StringComparer.Ordinal)];
    }

    // Walks the JSON as BundleReferencePreProcessor.RewriteReferences does (src/Application/Ignixa.Application/Features/Bundle/).
    private void Rewrite(JsonNode? node, string filePath, List<string> unresolved)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["reference"] is JsonValue value && value.TryGetValue(out string? reference))
                {
                    if (Resolve(reference, filePath) is { } resolved)
                    {
                        obj["reference"] = resolved;
                    }
                    else if (reference.StartsWith(UrnScheme, StringComparison.Ordinal))
                    {
                        unresolved.Add(reference);
                    }
                }

                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    Rewrite(property.Value, filePath, unresolved);
                }

                break;
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    Rewrite(item, filePath, unresolved);
                }

                break;
        }
    }

    /// <summary>The relative reference <paramref name="reference"/> names in the Bundle at <paramref name="filePath"/>, else in the folder, else null.</summary>
    private string? Resolve(string reference, string filePath)
    {
        if (Key(reference) is not { } key)
        {
            return null;
        }

        return _byBundle.TryGetValue((filePath, key), out string? inBundle)
            ? inBundle
            : _byFolder.GetValueOrDefault(key);
    }

    /// <summary>
    /// The text a fullUrl and a reference to it share: a <c>urn:</c> whole; an absolute URL as the reference parser reads it,
    /// base, type and id (so a <c>_history</c> suffix or the host's case does not matter), or whole when it has no
    /// <c>Type/id</c> tail; null for anything else, such as a relative reference.
    /// </summary>
    private string? Key(string text)
    {
        if (text.StartsWith(UrnScheme, StringComparison.Ordinal))
        {
            return text;
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            return null;
        }

        ReferenceSearchValue parsed = _parser.Parse(text);
        return parsed.BaseUri is null ? text : parsed.ToString();
    }

    /// <summary>Maps <paramref name="key"/> to <paramref name="target"/>, or to null (ambiguous) when it already names another resource.</summary>
    private static void Add<TKey>(Dictionary<TKey, string?> map, TKey key, string target)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out string? existing))
        {
            map.Add(key, target);
        }
        else if (!string.Equals(existing, target, StringComparison.Ordinal))
        {
            map[key] = null;
        }
    }
}

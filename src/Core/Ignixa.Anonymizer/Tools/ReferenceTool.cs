// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Ignixa.Abstractions;

namespace Ignixa.Anonymizer.Tools;

/// <summary>
/// Transforms FHIR reference IDs (literal, internal, URN) using a provided transformation function.
/// </summary>
internal class ReferenceTool
{
    private const string InternalReferencePrefix = "#";

    // Cache regex lists per schema to avoid rebuilding for every call
    private static readonly ConcurrentDictionary<IReadOnlySet<string>, List<Regex>> RegexCache = new();

    private static readonly Regex UrnOidRegex = new(@"^(?<prefix>urn:oid:)(?<id>[0-2](\.(0|[1-9][0-9]*))+)(?<suffix>)$");
    private static readonly Regex UrnUuidRegex = new(@"^(?<prefix>urn:uuid:)(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})(?<suffix>)$");

    private static List<Regex> GetLiteralReferenceRegexes(IReadOnlySet<string> resourceTypeNames)
    {
        return RegexCache.GetOrAdd(resourceTypeNames, names =>
        [
            new Regex(@"^(?<prefix>((http|https)://([A-Za-z0-9\\\/\.\:\%\$])*)?("
                + string.Join("|", names)
                + @")\/)(?<id>[A-Za-z0-9\-\.]{1,64})(?<suffix>\/_history\/[A-Za-z0-9\-\.]{1,64})?$"),
            UrnOidRegex,
            UrnUuidRegex
        ]);
    }

    public static string TransformReferenceId(string reference, IFhirSchemaProvider schema, Func<string, string> transformation)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return reference;
        }

        if (reference.StartsWith(InternalReferencePrefix))
        {
            var internalId = reference[InternalReferencePrefix.Length..];
            return $"{InternalReferencePrefix}{transformation(internalId)}";
        }

        var regexes = GetLiteralReferenceRegexes(schema.ResourceTypeNames);
        foreach (var regex in regexes)
        {
            var match = regex.Match(reference);
            if (match.Success)
            {
                var group = match.Groups["id"];
                var newId = transformation(group.Value);
                return $"{match.Groups["prefix"].Value}{newId}{match.Groups["suffix"].Value}";
            }
        }

        return transformation(reference);
    }
}

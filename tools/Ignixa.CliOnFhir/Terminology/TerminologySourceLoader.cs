// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Text.Json;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>Turns the JSON files of one source into a <see cref="LoadedSource"/>, reading only CodeSystem, ValueSet and ConceptMap.</summary>
internal static class TerminologySourceLoader
{
    public const string UnusableCode = "terminology-resource-unusable";

    private const int ProblemSamples = 3;

    public static LoadedSource Load(TerminologySourceInfo info, IReadOnlyList<string> fhirVersions, IEnumerable<TerminologyFile> files, int filesIgnored, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(files);

        var stopwatch = Stopwatch.StartNew();
        List<CodeSystemEntry> codeSystems = [];
        List<ValueSetEntry> valueSets = [];
        List<ConceptMapEntry> conceptMaps = [];
        List<string> unusable = [];
        int read = 0;
        int other = 0;
        foreach (TerminologyFile file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            read++;
            string? type;
            string? id;
            try
            {
                (type, id) = ResourceSniffer.Sniff(file.Json.Span);
            }
            catch (JsonException)
            {
                unusable.Add(file.Path);
                continue;
            }

            if (type is not ("CodeSystem" or "ValueSet" or "ConceptMap"))
            {
                other++;
                continue;
            }

            string? problem;
            try
            {
                problem = Add(type, id, file, info, codeSystems, valueSets, conceptMaps);
            }
            catch (JsonException ex)
            {
                problem = ex.GetType().Name;
            }

            if (problem is not null)
            {
                unusable.Add(file.Path);
            }
        }

        List<IssueItem> problems = [];
        if (unusable.Count > 0)
        {
            string samples = string.Join(", ", unusable.Take(ProblemSamples).Select(path => info.EvidencePrefix + TerminologyNames.Path(path)));
            problems.Add(new IssueItem(
                "warning",
                UnusableCode,
                $"{unusable.Count} files of {info.Name} are not usable terminology resources (not a JSON object, or a CodeSystem, ValueSet or ConceptMap without a url) and were not loaded: {samples}{(unusable.Count > ProblemSamples ? ", ..." : string.Empty)}.",
                info.EvidencePrefix + TerminologyNames.Path(unusable[0])));
        }

        return new LoadedSource(
            info,
            fhirVersions,
            codeSystems,
            valueSets,
            conceptMaps,
            new SourceCounts(read, other, unusable.Count, filesIgnored),
            problems,
            stopwatch.ElapsedMilliseconds);
    }

    private static string? Add(string type, string? id, TerminologyFile file, TerminologySourceInfo info, List<CodeSystemEntry> codeSystems, List<ValueSetEntry> valueSets, List<ConceptMapEntry> conceptMaps)
    {
        using JsonDocument document = JsonDocument.Parse(file.Json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return "not an object";
        }

        var origin = new ResourceOrigin(info, type, id, file.Path, file.Line, file.Offset);
        switch (type)
        {
            case "CodeSystem":
                (CodeSystemEntry? codeSystem, string? codeSystemProblem) = CodeSystemReader.Read(document.RootElement, origin);
                if (codeSystem is not null)
                {
                    codeSystems.Add(codeSystem);
                }

                return codeSystemProblem;
            case "ValueSet":
                (ValueSetEntry? valueSet, string? valueSetProblem) = ValueSetReader.Read(document.RootElement, origin);
                if (valueSet is not null)
                {
                    valueSets.Add(valueSet);
                }

                return valueSetProblem;
            default:
                (ConceptMapEntry? conceptMap, string? conceptMapProblem) = ConceptMapReader.Read(document.RootElement, origin);
                if (conceptMap is not null)
                {
                    conceptMaps.Add(conceptMap);
                }

                return conceptMapProblem;
        }
    }
}

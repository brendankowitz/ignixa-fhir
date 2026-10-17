// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology.Tools;

namespace Ignixa.CliOnFhir.Terminology.DrugClasses;

/// <summary><c>terminology</c> classes / <c>cli-on-fhir term classes</c>: the drug classes of one RxNorm code, per source.</summary>
public static class TermClasses
{
    private const int MaxNext = 4;

    public static Task<ToolEnvelope<DrugClassesFacts?>> ClassesAsync(FolderSession session, string? code, string? source, CancellationToken cancellationToken) =>
        DrugClassCall.RunAsync<DrugClassesFacts>(
            session,
            async reads =>
            {
                string rxcui = TermArguments.Required(code, "code");
                if (!RxClassJson.IsRxcui(rxcui))
                {
                    throw ToolRequestException.Error("invalid-parameter", "'code' must be an RxNorm code (an RxCUI, digits only), for example 866412.");
                }

                IReadOnlyList<DrugClassSource> sources = DrugClassSource.Parse(source, [DrugClassSource.Atc, DrugClassSource.Va]);
                JsonObject[] bodies = await Task.WhenAll(sources.Select(each => reads.GetAsync("class/byRxcui.json", [("rxcui", rxcui), .. each.ClassesOf], cancellationToken)));
                List<DrugClass> all = [.. sources
                    .Zip(bodies, (each, body) => RxClassJson.DrugClasses(body, each.Name))
                    .SelectMany(classes => classes)
                    .DistinctBy(item => (item.ClassSource, item.ClassId))];

                List<OmittedItem> omitted = [];
                RxClassSource provenance = reads.Source();
                var facts = new DrugClassesFacts(rxcui, [.. sources.Select(each => each.Name)], all.Count, TermText.Take(all, "classes", omitted), provenance);
                List<AbsentItem> absent = all.Count == 0
                    ? [new AbsentItem($"drug classes of RxNorm {rxcui} in {string.Join(", ", facts.ClassSources)}", $"RxClass at {provenance.Server}", provenance.RetrievedAt)]
                    : [];
                List<NextCall> next = [.. all
                    .DistinctBy(item => item.ClassSource)
                    .Concat(all)
                    .Distinct()
                    .Take(MaxNext)
                    .Select(item => TermNext.Call("class-members", new Dictionary<string, object> { ["classId"] = item.ClassId, ["source"] = item.ClassSource }))];
                return new TerminologyAnswer<DrugClassesFacts>(facts, [], omitted, absent, reads.Issues()) { Next = next };
            },
            cancellationToken);
}

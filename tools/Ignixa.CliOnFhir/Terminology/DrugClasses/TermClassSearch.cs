// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology.Tools;

namespace Ignixa.CliOnFhir.Terminology.DrugClasses;

/// <summary>
/// <c>terminology</c> class-search / <c>cli-on-fhir term class-search</c>: drug classes by name. Each source's class list
/// (RxClass allClasses) is fetched once and cached, and the text is matched here, so it is never sent anywhere. Classes
/// of sources with product members (ATC, VA) rank before equally good EPC and MoA ones, since only they match orders.
/// </summary>
public static class TermClassSearch
{
    public const string NoMatchCode = "rxclass-no-match";

    public static Task<ToolEnvelope<DrugClassSearchFacts?>> SearchAsync(FolderSession session, string? text, string? source, int? count, int offset, CancellationToken cancellationToken) =>
        DrugClassCall.RunAsync<DrugClassSearchFacts>(
            session,
            async reads =>
            {
                string query = TermArguments.Required(text, "text");
                IReadOnlyList<DrugClassSource> sources = DrugClassSource.Parse(source, DrugClassSource.All);
                List<IssueItem> issues = [];
                int pageSize = Paging.Resolve(count, offset, issues);
                JsonObject[] bodies = await Task.WhenAll(sources.Select(each => reads.GetAsync("allClasses.json", [("classTypes", each.ClassType)], cancellationToken)));

                var match = ClassNameMatch.For(query);
                List<DrugClass> ranked = [.. sources
                    .Select((each, index) => (Index: index, Classes: RxClassJson.Classes(bodies[index], each.Name)))
                    .SelectMany(list => list.Classes.Select(item => (Item: item, list.Index, Score: match.Score(item.ClassName))))
                    .Where(candidate => candidate.Score != ClassNameMatch.NoMatch)
                    .OrderBy(candidate => candidate.Score)
                    .ThenByDescending(candidate => sources[candidate.Index].HasProducts)
                    .ThenBy(candidate => candidate.Item.ClassName!.Length)
                    .ThenBy(candidate => candidate.Index)
                    .ThenBy(candidate => candidate.Item.ClassId, StringComparer.Ordinal)
                    .Select(candidate => candidate.Item)];

                List<DrugClass> page = [.. ranked.Skip(offset).Take(pageSize)];
                RxClassSource provenance = reads.Source();
                var facts = new DrugClassSearchFacts([.. sources.Select(each => each.Name)], match.SearchedAs, ranked.Count, offset, page.Count, page, provenance);
                issues.AddRange(Paging.BeyondTotal(offset, ranked.Count, "classes"));
                issues.AddRange(reads.Issues());

                List<OmittedItem> omitted = [];
                List<NextCall> next = [.. page.Take(1).Select(best => TermNext.Call("class-members", new Dictionary<string, object> { ["classId"] = best.ClassId, ["source"] = best.ClassSource }))];
                int remaining = ranked.Count - offset - page.Count;
                if (remaining > 0)
                {
                    omitted.Add(new OmittedItem($"{remaining} more classes", "limit"));
                    var args = new Dictionary<string, object> { ["text"] = query, ["offset"] = offset + page.Count, ["count"] = pageSize };
                    if (source is not null)
                    {
                        args["source"] = string.Join(',', facts.ClassSources);
                    }

                    next.Add(TermNext.Call("class-search", args));
                }

                List<AbsentItem> absent = [];
                if (ranked.Count == 0)
                {
                    absent.Add(new AbsentItem($"drug classes whose names contain every word of the text in {string.Join(", ", facts.ClassSources)}", $"RxClass at {provenance.Server}", provenance.RetrievedAt));
                    issues.Add(new IssueItem(
                        "information",
                        NoMatchCode,
                        "No class name matched. RxClass names are formal ('HMG CoA reductase inhibitors' for statins, 'Beta blocking agents'); try fewer or formal words, or operation=classes with an RxNorm code the folder uses.",
                        Location: null));
                }

                return new TerminologyAnswer<DrugClassSearchFacts>(facts, [], omitted, absent, issues) { Next = next };
            },
            cancellationToken);
}

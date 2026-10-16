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
/// <c>terminology</c> class-members / <c>cli-on-fhir term class-members</c>: the RxNorm codes of one drug class (its
/// subclasses included), by default only those the folder's medication resources carry, with the searches that find
/// the orders.
/// </summary>
public static class TermClassMembers
{
    public const string FolderScope = "folder";
    public const string AllScope = "all";
    public const string ProductLevel = "product";
    public const string IngredientLevel = "ingredient";
    public const string RxNorm = "http://www.nlm.nih.gov/research/umls/rxnorm";
    public const string EmptyClassCode = "rxclass-class-empty";
    public const string IngredientsOnlyCode = "rxclass-ingredients-only";

    /// <summary>The types whose RxNorm codes the folder scope compares: orders carry them directly or through a Medication.</summary>
    public static IReadOnlyList<string> MedicationTypes { get; } = ["MedicationRequest", "MedicationStatement", "MedicationAdministration", "MedicationDispense", "Medication"];

    private const int DefaultCount = 50;
    private const int FactsBudget = 6 * 1024;
    private const int MaxCodesInSearch = 30;

    public static Task<ToolEnvelope<DrugClassMembersFacts?>> MembersAsync(FolderSession session, string? classId, string? source, string? scope, int? count, int offset, CancellationToken cancellationToken) =>
        DrugClassCall.RunAsync<DrugClassMembersFacts>(
            session,
            async reads =>
            {
                string id = TermArguments.Required(classId, "classId");
                if (!RxClassJson.IsClassId(id))
                {
                    throw ToolRequestException.Error("invalid-parameter", "'classId' must be an RxClass class id (letters and digits), for example C07AB; find one with operation=class-search.");
                }

                IReadOnlyList<DrugClassSource> named = DrugClassSource.Parse(TermArguments.Required(source, "source"), []);
                DrugClassSource classSource = named.Count == 1 ? named[0] : throw ToolRequestException.Error("invalid-parameter", $"class-members takes one 'source': {DrugClassSource.Names}.");
                string checkedScope = TermArguments.Optional(scope, "scope") ?? FolderScope;
                if (checkedScope is not (FolderScope or AllScope))
                {
                    throw ToolRequestException.Error("invalid-parameter", "'scope' is folder (the default: only codes the folder's medication resources carry) or all.");
                }

                List<IssueItem> issues = [];
                int pageSize = Paging.Resolve(count ?? DefaultCount, offset, issues);
                List<DrugClassMember> members = await ReadMembersAsync(reads, classSource, id, cancellationToken);
                int products = members.Count(member => member.Level == ProductLevel);

                IReadOnlyDictionary<string, int>? folderCodes = checkedScope == FolderScope
                    ? FolderTerminology.ResourcesPerCode(session, RxNorm, MedicationTypes)
                    : null;
                List<DrugClassMember> inScope = folderCodes is null
                    ? members
                    : [.. members
                        .Where(member => folderCodes.ContainsKey(member.Rxcui))
                        .Select(member => member with { FolderResources = folderCodes[member.Rxcui] })
                        .OrderByDescending(member => member.FolderResources)
                        .ThenBy(member => member.Name, StringComparer.Ordinal)];

                List<OmittedItem> omitted = [];
                RxClassSource provenance = reads.Source();
                List<DrugClassMember> page = [.. inScope.Skip(offset).Take(pageSize)];
                (DrugClassMembersFacts facts, int kept) = TermText.FitPage(
                    page,
                    items => new DrugClassMembersFacts(id, classSource.Name, checkedScope, Rule(classSource, checkedScope), products, members.Count - products, folderCodes?.Count, inScope.Count, offset, items.Count, items, provenance),
                    "members",
                    omitted,
                    FactsBudget);

                issues.AddRange(Paging.BeyondTotal(offset, inScope.Count, "members"));
                issues.AddRange(reads.Issues());
                if (members.Count == 0)
                {
                    issues.Add(new IssueItem("warning", EmptyClassCode, $"RxClass lists no members for {classSource.Name} class {id}: the id may not be one of that source (ids are case-sensitive); find ids with operation=class-search.", Location: null));
                }

                if (!classSource.HasProducts)
                {
                    issues.Add(new IssueItem("warning", IngredientsOnlyCode, $"RxClass relates {classSource.Name} classes to ingredients only, while orders usually carry product codes (SCD, SBD, GPCK, BPCK), so the folder's products of this class are not matched. ATC and VA classes have product members.", Location: null));
                }

                List<NextCall> next = Searches(facts.Members);
                int remaining = inScope.Count - offset - kept;
                if (remaining > 0)
                {
                    omitted.Add(new OmittedItem($"{remaining} more members", "limit"));
                    next.Add(TermNext.Call("class-members", new Dictionary<string, object> { ["classId"] = id, ["source"] = classSource.Name, ["scope"] = checkedScope, ["offset"] = offset + kept, ["count"] = pageSize }));
                }

                List<AbsentItem> absent = inScope.Count > 0 ? []
                    : folderCodes is not null
                        ? [new AbsentItem($"RxNorm codes of {classSource.Name} class {id} in the folder", $"the {folderCodes.Count} distinct RxNorm codes of the folder's {string.Join(", ", MedicationTypes)} resources against {members.Count} class members from RxClass", session.Catalog.BuiltAt)]
                        : [new AbsentItem($"members of {classSource.Name} class {id}", $"RxClass at {provenance.Server}", provenance.RetrievedAt)];
                return new TerminologyAnswer<DrugClassMembersFacts>(facts, [], omitted, absent, issues) { Next = next };
            },
            cancellationToken);

    private static async Task<List<DrugClassMember>> ReadMembersAsync(RxClassReads reads, DrugClassSource source, string classId, CancellationToken cancellationToken)
    {
        Task<JsonObject>[] productReads = [.. source.Products.Select(relation => reads.GetAsync("classMembers.json", [("classId", classId), .. relation], cancellationToken))];
        Task<JsonObject>? ingredientRead = source.Ingredients is { } ingredients ? reads.GetAsync("classMembers.json", [("classId", classId), .. ingredients], cancellationToken) : null;
        JsonObject[] productBodies = await Task.WhenAll(productReads);
        JsonObject? ingredientBody = ingredientRead is null ? null : await ingredientRead;

        List<DrugClassMember> products = [.. productBodies
            .SelectMany(RxClassJson.Members)
            .DistinctBy(concept => concept.Rxcui)
            .OrderBy(concept => concept.Name, StringComparer.Ordinal)
            .Select(concept => new DrugClassMember(concept.Rxcui, concept.Name, concept.Tty, ProductLevel, null))];
        HashSet<string> productCodes = [.. products.Select(member => member.Rxcui)];
        IEnumerable<DrugClassMember> ingredientMembers = (ingredientBody is null ? [] : RxClassJson.Members(ingredientBody))
            .Where(concept => !productCodes.Contains(concept.Rxcui))
            .DistinctBy(concept => concept.Rxcui)
            .OrderBy(concept => concept.Name, StringComparer.Ordinal)
            .Select(concept => new DrugClassMember(concept.Rxcui, concept.Name, concept.Tty, IngredientLevel, null));
        return [.. products, .. ingredientMembers];
    }

    // Both medication shapes: medicationCodeableConcept through 'code', medicationReference through the chain.
    private static List<NextCall> Searches(IReadOnlyList<DrugClassMember> members)
    {
        if (members.Count == 0)
        {
            return [];
        }

        string codes = string.Join(',', members.Take(MaxCodesInSearch).Select(member => $"{RxNorm}|{member.Rxcui}"));
        string scope = members.Count > MaxCodesInSearch ? $"the first {MaxCodesInSearch} codes listed" : "these codes";
        return
        [
            new NextCall("search_fhir_resources", new Dictionary<string, object>(StringComparer.Ordinal) { ["resourceType"] = "MedicationRequest", ["query"] = $"code={codes}" }, $"Orders whose medicationCodeableConcept is one of {scope}; add status=active, _sort=-authoredon, summary=count or columns as needed."),
            new NextCall("search_fhir_resources", new Dictionary<string, object>(StringComparer.Ordinal) { ["resourceType"] = "MedicationRequest", ["query"] = $"medication:Medication.code={codes}" }, $"Orders whose medicationReference names a Medication with one of {scope}."),
        ];
    }

    private static string Rule(DrugClassSource source, string scope)
    {
        string products = source.HasProducts ? $"products from RxClass classMembers {string.Join(" and ", source.Products.Select(Describe))}" : "no product members";
        string ingredients = source.Ingredients is { } relation ? $"ingredients from classMembers {Describe(relation)}" : "no ingredient members";
        string folder = scope == FolderScope ? $"; kept when the code is in an RxNorm token search-index entry of the folder's {string.Join(", ", MedicationTypes)} resources" : string.Empty;
        return $"{products}; {ingredients}; subclasses included{folder}.";
    }

    private static string Describe(IReadOnlyList<(string Name, string Value)> relation) => string.Join('&', relation.Select(parameter => $"{parameter.Name}={parameter.Value}"));
}

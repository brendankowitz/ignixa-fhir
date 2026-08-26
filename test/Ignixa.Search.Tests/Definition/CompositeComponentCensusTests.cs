using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Search.Tests.Definition;

/// <summary>
/// A census of composite search parameter component resolution across every shipped FHIR version.
/// </summary>
/// <remarks>
/// <para>
/// A composite whose component cannot be resolved indexes nothing, and the search that uses it
/// returns an empty bundle with HTTP 200. That is indistinguishable from "no matches" at the API, and
/// it was invisible to the resource-backed parity corpus for a structural reason: the corpus hands
/// one definition manager to both of its indexers, so both sides drop the same composite and the
/// entry-list comparison scores it as agreement.
/// </para>
/// <para>
/// This census reads the production definition manager directly instead. Every component either
/// resolves or is named in <see cref="KnownCompositeComponentDivergences"/> with a reason, and the
/// table is checked against live state in both directions so it cannot outlive what it describes.
/// </para>
/// </remarks>
public class CompositeComponentCensusTests
{
    public static TheoryData<FhirVersion> Versions =>
        new(FhirVersion.Stu3, FhirVersion.R4, FhirVersion.R4B, FhirVersion.R5, FhirVersion.R6);

    /// <summary>
    /// Composites the census must find in each version, so a definition loader that stopped producing
    /// them cannot satisfy every other assertion here with nothing to examine.
    /// </summary>
    private static IReadOnlyDictionary<FhirVersion, int> MinimumComposites { get; } =
        new Dictionary<FhirVersion, int>
        {
            [FhirVersion.Stu3] = 12,
            [FhirVersion.R4] = 46,
            [FhirVersion.R4B] = 46,
            [FhirVersion.R5] = 26,
            [FhirVersion.R6] = 28,
        };

    [Theory]
    [MemberData(nameof(Versions))]
    public void GivenShippedComposites_WhenCensused_ThenEveryComponentResolvesOrIsDocumented(
        FhirVersion version)
    {
        var documented = KnownCompositeComponentDivergences.All
            .Where(divergence => divergence.Version == version)
            .Select(divergence => divergence.Key)
            .ToHashSet();

        var undocumented = Unresolved(version)
            .Where(component => !documented.Contains(component.Key))
            .Select(component =>
                $"{component.CompositeUrl} component {component.ComponentIndex} references "
                + $"{component.DefinitionUrl}, which {version} does not publish. That composite indexes "
                + "nothing, so a search on it returns an empty bundle with HTTP 200. Repair the "
                + "reference in CompositeComponentDefinitionRepairs, or record why it stays broken in "
                + "KnownCompositeComponentDivergences.")
            .ToArray();

        undocumented.ShouldBeEmpty(string.Join(Environment.NewLine + Environment.NewLine, undocumented));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void GivenTheDivergenceTable_WhenCensused_ThenNoEntryDescribesAResolvedComponent(
        FhirVersion version)
    {
        var stillUnresolved = Unresolved(version)
            .Select(component => component.Key)
            .ToHashSet();

        var stale = KnownCompositeComponentDivergences.All
            .Where(divergence => divergence.Version == version)
            .Where(divergence => !stillUnresolved.Contains(divergence.Key))
            .Select(divergence =>
                $"{divergence} is recorded as unresolvable, but it resolves now. Either the shipped "
                + "definitions gained the parameter or a repair covers it; delete the entry.")
            .ToArray();

        stale.ShouldBeEmpty(string.Join(Environment.NewLine + Environment.NewLine, stale));
    }

    /// <summary>
    /// Each entry claims the definition URL is absent from the shipped definitions. Checking that
    /// separately from resolution keeps the reason honest: a component could stop resolving for a
    /// different cause, and the entry would then be describing something that is no longer true.
    /// </summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public void GivenTheDivergenceTable_WhenCensused_ThenEveryNamedDefinitionUrlIsStillUnpublished(
        FhirVersion version)
    {
        var published = Definitions(version).AllSearchParameters
            .Select(parameter => parameter.Url.OriginalString)
            .ToHashSet(StringComparer.Ordinal);

        var wrong = KnownCompositeComponentDivergences.All
            .Where(divergence => divergence.Version == version)
            .Where(divergence => published.Contains(divergence.DefinitionUrl))
            .Select(divergence =>
                $"{divergence} says {version} does not publish {divergence.DefinitionUrl}, and it does. "
                + "The reason recorded for this entry is no longer the reason it fails.")
            .ToArray();

        wrong.ShouldBeEmpty(string.Join(Environment.NewLine + Environment.NewLine, wrong));
    }

    [Fact]
    public void GivenTheDivergenceTable_WhenCensused_ThenEveryReasonIsSubstantive()
    {
        var thin = KnownCompositeComponentDivergences.All
            .Where(divergence => divergence.Reason.Trim().Length < 80)
            .Select(divergence => $"{divergence} - reason is too short to be a reason.")
            .ToArray();

        thin.ShouldBeEmpty(string.Join(Environment.NewLine, thin));
    }

    /// <summary>
    /// Every repair must still be needed and must still land somewhere. A repair whose target URL is
    /// itself unpublished would silently do nothing, and one whose source URL the specification has
    /// since published is dead weight that hides a real change in the data.
    /// </summary>
    [Fact]
    public void GivenTheRepairTable_WhenCensused_ThenEveryRepairIsStillNeededAndStillLands()
    {
        var failures = new List<string>();

        foreach (var (version, definitionUrl, repairedUrl) in CompositeComponentDefinitionRepairs.All)
        {
            var published = Definitions(version).AllSearchParameters
                .Select(parameter => parameter.Url.OriginalString)
                .ToHashSet(StringComparer.Ordinal);

            if (published.Contains(definitionUrl))
            {
                failures.Add(
                    $"{version} repairs {definitionUrl}, but {version} now publishes it. The repair is "
                    + "no longer needed and is masking whatever the definitions actually say.");
            }

            if (!published.Contains(repairedUrl.OriginalString))
            {
                failures.Add(
                    $"{version} repairs {definitionUrl} to {repairedUrl}, which {version} does not "
                    + "publish. The repair resolves nothing.");
            }

            bool referenced = Definitions(version).AllSearchParameters
                .Where(parameter => parameter.Component is { Count: > 0 })
                .SelectMany(parameter => parameter.Component)
                .Any(component => string.Equals(
                    component.DefinitionUrl?.OriginalString,
                    definitionUrl,
                    StringComparison.Ordinal));

            if (!referenced)
            {
                failures.Add(
                    $"{version} repairs {definitionUrl}, and no {version} composite component "
                    + "references it. The repair fires on nothing, so it neither fixes anything nor "
                    + "fails when the thing it claims to fix changes.");
            }
        }

        CompositeComponentDefinitionRepairs.All.ShouldNotBeEmpty(
            "The repair table is empty, so every assertion about it passes without examining anything.");
        failures.ShouldBeEmpty(string.Join(Environment.NewLine + Environment.NewLine, failures));
    }

    /// <summary>
    /// The four STU3 Observation composites the repair exists for. Named individually because these
    /// are the parameters whose silent failure the census was built to catch: with the component
    /// unresolved, <c>Observation?code-value-quantity=</c> returns an empty bundle and HTTP 200.
    /// </summary>
    [Theory]
    [InlineData("http://hl7.org/fhir/SearchParameter/Observation-code-value-concept")]
    [InlineData("http://hl7.org/fhir/SearchParameter/Observation-code-value-date")]
    [InlineData("http://hl7.org/fhir/SearchParameter/Observation-code-value-quantity")]
    [InlineData("http://hl7.org/fhir/SearchParameter/Observation-code-value-string")]
    public void GivenAStu3ObservationComposite_WhenResolved_ThenBothComponentsCarryTheirType(
        string compositeUrl)
    {
        SearchParameterInfo composite = Definitions(FhirVersion.Stu3).AllSearchParameters
            .Single(parameter => string.Equals(
                parameter.Url.OriginalString,
                compositeUrl,
                StringComparison.Ordinal));

        composite.Component.Count.ShouldBe(2);

        for (int index = 0; index < composite.Component.Count; index++)
        {
            composite.Component[index].ResolvedSearchParameter.ShouldNotBeNull(
                $"STU3 {compositeUrl} component {index} has no resolved search parameter, so the "
                + "composite indexes nothing and a search on it returns an empty bundle with HTTP 200.");
        }

        composite.Component[0].ResolvedSearchParameter!.Code.ShouldBe("code");
        composite.Component[0].ResolvedSearchParameter!.Type.ShouldBe(SearchParamType.Token);
    }

    private static IEnumerable<CompositeComponentSite> Unresolved(FhirVersion version)
    {
        var definitions = Definitions(version);
        int composites = 0;

        foreach (SearchParameterInfo parameter in definitions.AllSearchParameters
                     .Where(parameter => parameter.Component is { Count: > 0 })
                     .OrderBy(parameter => parameter.Url.OriginalString, StringComparer.Ordinal))
        {
            composites++;

            for (int index = 0; index < parameter.Component.Count; index++)
            {
                SearchParameterComponentInfo component = parameter.Component[index];
                if (component.ResolvedSearchParameter is null)
                {
                    yield return new CompositeComponentSite(
                        version,
                        parameter.Url.OriginalString,
                        index,
                        component.DefinitionUrl?.OriginalString ?? "(none)");
                }
            }
        }

        if (composites < MinimumComposites[version])
        {
            throw new InvalidOperationException(
                $"{version} produced {composites} composite search parameters, below the floor of "
                + $"{MinimumComposites[version]}. The census would otherwise pass by examining nothing. "
                + "Raise the floor when the definitions genuinely grow; never lower it to accommodate a loss.");
        }
    }

    private static SearchParameterDefinitionManager Definitions(FhirVersion version) =>
        new(version.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);

    private sealed record CompositeComponentSite(
        FhirVersion Version,
        string CompositeUrl,
        int ComponentIndex,
        string DefinitionUrl)
    {
        public (FhirVersion, string, int) Key => (Version, CompositeUrl, ComponentIndex);
    }
}

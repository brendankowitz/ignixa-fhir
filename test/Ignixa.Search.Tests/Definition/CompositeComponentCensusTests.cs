using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Specification.Extensions;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Search.Tests.Definition;

/// <summary>
/// A census of composite search parameter component resolution across every shipped FHIR version.
/// </summary>
/// <remarks>
/// <para>
/// A composite whose component cannot be resolved indexes nothing, and the search that uses it returns an
/// empty bundle with HTTP 200 - indistinguishable from "no matches" at the API. It was invisible to the
/// resource-backed parity corpus for a structural reason: that corpus hands one definition manager to
/// both of its indexers, so both sides drop the same composite and the comparison scores it as agreement.
/// </para>
/// <para>
/// This census reads the production definition manager directly instead. Every component either resolves
/// or is named in <see cref="KnownCompositeComponentDivergences"/> with a reason, and the table is checked
/// against live state in both directions so it cannot outlive what it describes.
/// </para>
/// </remarks>
public class CompositeComponentCensusTests
{
    /// <summary>
    /// The floor on what an entry's reason contributes beyond the shared preamble, in characters.
    /// </summary>
    private const int MinimumOwnReasonLength = 50;

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
    /// different cause, leaving the entry describing something no longer true.
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

    /// <summary>
    /// Every reason has to say something this entry's own; an entry reduced to a placeholder turns the
    /// table back into a suppression list.
    /// </summary>
    /// <remarks>
    /// Measured after stripping <see cref="KnownCompositeComponentDivergences.SharedPreambles"/>, which is
    /// what makes this able to fail at all: each preamble is around 140 characters, so measuring
    /// <c>Reason</c> whole gave every entry more than the budget before it said anything of its own. Own
    /// lengths across the 31 entries run 59 to 497, so the floor sits at 50 - below the shortest, and far
    /// above a placeholder.
    /// </remarks>
    [Fact]
    public void GivenTheDivergenceTable_WhenCensused_ThenEveryReasonIsSubstantive()
    {
        var thin = KnownCompositeComponentDivergences.All
            .Select(divergence => (Divergence: divergence, Own: OwnReason(divergence.Reason)))
            .Where(entry => entry.Own.Length < MinimumOwnReasonLength)
            .Select(entry =>
                $"{entry.Divergence} - {entry.Own.Length} characters beyond the shared preamble is not a "
                + $"reason: '{entry.Own}'.")
            .ToArray();

        thin.ShouldBeEmpty(string.Join(Environment.NewLine, thin));
    }

    /// <summary>
    /// What an entry's reason says beyond the preamble every entry inherits.
    /// </summary>
    private static string OwnReason(string reason)
    {
        foreach (string preamble in KnownCompositeComponentDivergences.SharedPreambles)
        {
            reason = reason.Replace(preamble, string.Empty, StringComparison.Ordinal);
        }

        return reason.Trim();
    }

    /// <summary>
    /// Every repair must still be needed and must still land somewhere: a repair whose target URL is
    /// itself unpublished silently does nothing, and one whose source URL has since been published is
    /// dead weight hiding a real change in the data.
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

    /// <summary>
    /// Every composite component <paramref name="version"/> ships that resolves to nothing.
    /// </summary>
    /// <remarks>
    /// Eager, deliberately: the <see cref="MinimumComposites"/> floor is what stops this census passing on
    /// an empty enumeration, and as the last statement of a lazy iterator it would run only for a caller
    /// that enumerated to the end - a caller written with <c>Any()</c> or <c>First()</c> would skip it in
    /// silence. Materialising first puts the floor ahead of any result being handed out.
    /// </remarks>
    private static IReadOnlyList<CompositeComponentSite> Unresolved(FhirVersion version)
    {
        SearchParameterInfo[] composites =
        [
            .. Definitions(version).AllSearchParameters
                .Where(parameter => parameter.Component is { Count: > 0 })
                .OrderBy(parameter => parameter.Url.OriginalString, StringComparer.Ordinal)
        ];

        if (composites.Length < MinimumComposites[version])
        {
            throw new InvalidOperationException(
                $"{version} produced {composites.Length} composite search parameters, below the floor of "
                + $"{MinimumComposites[version]}. The census would otherwise pass by examining nothing. "
                + "Raise the floor when the definitions genuinely grow; never lower it to accommodate a loss.");
        }

        return
        [
            .. composites.SelectMany(parameter => parameter.Component
                .Select((component, index) => (component, index))
                .Where(entry => entry.component.ResolvedSearchParameter is null)
                .Select(entry => new CompositeComponentSite(
                    version,
                    parameter.Url.OriginalString,
                    entry.index,
                    entry.component.DefinitionUrl?.OriginalString ?? "(none)")))
        ];
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

using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Specification;
using Ignixa.Specification.ValueSets.Normative;
using Ignixa.Specification.Extensions;

namespace Ignixa.Search.Tests.Indexing;

/// <summary>
/// A static census of what <c>FhirElementToSearchValueConverterManager</c> registers, against a
/// vendored snapshot of what <c>microsoft/fhir-server</c> registers.
/// </summary>
/// <remarks>
/// <para>
/// This exists because a differential corpus structurally cannot answer the question. The
/// resource-backed parity harness hands one converter manager, one definition manager and one set of
/// indexer statics to both of its indexers, so when both sides skip an element that is one object
/// deciding once - a missing converter and a correct skip look identical to it. That blind spot is
/// how the <c>canonical</c> gap survived until somebody switched a logger on, and it is why 115
/// recorded skips sat unadjudicated behind a pin that measured them without judging them.
/// </para>
/// <para>
/// A registration census has no such blind spot, because it compares two independently authored sets
/// rather than one set with itself. It answers the question once for the whole class, it fails when
/// upstream adds a converter Ignixa lacks, and it needs nobody to notice a log line.
/// </para>
/// </remarks>
public class ConverterRegistrationCensusTests
{
    /// <summary>
    /// The converter set is version-independent - <c>SearchIndexerFactory</c> discovers the same
    /// exported types whatever schema it is given - so one version is enough to enumerate it. R4 is
    /// used because the converters that take a schema still need a real one to construct.
    /// </summary>
    private static IReadOnlyList<ConverterRegistration> IgnixaRegistrations { get; } = Enumerate();

    private static IReadOnlySet<ConverterPair> IgnixaPairs { get; } =
        IgnixaRegistrations.Select(registration => registration.Pair).ToHashSet();

    private static IElementToSearchValueConverterManager Manager { get; } =
        SearchIndexerFactory.CreateIndexingComponents(
            FhirVersion.R4.GetSchemaProvider(),
            NullFhirBaseUriProvider.Instance).ConverterManager;

    [Fact]
    public void GivenUpstreamRegistrations_WhenCensused_ThenEveryPairIsCoveredOrDocumented()
    {
        var undocumented = new List<string>();
        var staleEntries = new List<string>();

        foreach (var registration in UpstreamConverterRegistrations.All)
        {
            bool covered = Manager.TryGetConverter(
                registration.FhirType,
                registration.SearchValueType,
                out _);
            bool documented = KnownConverterDivergences.MissingFromIgnixa.ContainsKey(registration.Pair);

            if (!covered && !documented)
            {
                undocumented.Add(
                    $"{registration.Pair} - registered upstream by {registration.ConverterName}, "
                    + "absent from Ignixa and absent from KnownConverterDivergences.MissingFromIgnixa. "
                    + "Either port the converter or record why this codebase does not want it.");
            }

            if (covered && documented)
            {
                staleEntries.Add(
                    $"{registration.Pair} - KnownConverterDivergences.MissingFromIgnixa says Ignixa "
                    + "does not register this, but the manager resolves it. The divergence closed; "
                    + "delete the entry.");
            }
        }

        undocumented.Concat(staleEntries).ShouldBeEmpty(Render(undocumented.Concat(staleEntries)));
    }

    [Fact]
    public void GivenIgnixaRegistrations_WhenCensused_ThenEveryAdditionIsDocumented()
    {
        var undocumented = IgnixaRegistrations
            .Where(registration => !UpstreamConverterRegistrations.Pairs.Contains(registration.Pair))
            .Where(registration => !KnownConverterDivergences.AdditionalInIgnixa.ContainsKey(registration.Pair))
            .Select(registration =>
                $"{registration.Pair} - registered by Ignixa's {registration.ConverterName}, absent "
                + "from the upstream snapshot and from KnownConverterDivergences.AdditionalInIgnixa. "
                + "Record why Ignixa indexes something upstream does not, or refresh the snapshot if "
                + "upstream has since added it.")
            .ToArray();

        undocumented.ShouldBeEmpty(Render(undocumented));
    }

    [Fact]
    public void GivenTheDivergenceTable_WhenCensused_ThenNoEntryDescribesSomethingThatIsNotADivergence()
    {
        var wrong = new List<string>();

        foreach (var (pair, _) in KnownConverterDivergences.MissingFromIgnixa)
        {
            if (!UpstreamConverterRegistrations.Pairs.Contains(pair))
            {
                wrong.Add(
                    $"{pair} - listed as missing from Ignixa, but the upstream snapshot does not "
                    + "register it either, so there is nothing to diverge from.");
            }
        }

        foreach (var (pair, _) in KnownConverterDivergences.AdditionalInIgnixa)
        {
            if (UpstreamConverterRegistrations.Pairs.Contains(pair))
            {
                wrong.Add(
                    $"{pair} - listed as an Ignixa addition, but the upstream snapshot registers it "
                    + "too, so it is shared rather than additional.");
            }

            if (!IgnixaPairs.Contains(pair))
            {
                wrong.Add(
                    $"{pair} - listed as an Ignixa addition, but Ignixa does not register it.");
            }
        }

        wrong.ShouldBeEmpty(Render(wrong));
    }

    /// <summary>
    /// Every reason in the table has to say something. An entry whose reason is a placeholder
    /// documents nothing and turns the table back into a suppression list.
    /// </summary>
    [Fact]
    public void GivenTheDivergenceTable_WhenCensused_ThenEveryReasonIsSubstantive()
    {
        var thin = KnownConverterDivergences.MissingFromIgnixa
            .Concat(KnownConverterDivergences.AdditionalInIgnixa)
            .Where(entry => entry.Value.Trim().Length < 80)
            .Select(entry => $"{entry.Key} - reason is too short to be a reason: '{entry.Value.Trim()}'")
            .ToArray();

        thin.ShouldBeEmpty(Render(thin));
    }

    /// <summary>
    /// Every FHIR type <c>InferSearchParamTypeFromFhirType</c> can name must have a converter for the
    /// search value type that inference implies, or the fallback is dead code that produces a
    /// <c>FhirElementTypeNotSupported</c> skip one line later.
    /// </summary>
    /// <remarks>
    /// Upstream has no inference table at all - <c>TypedElementSearchIndexer</c> looks the converter up
    /// against the search parameter's declared type and skips on a miss. Ignixa's table is an addition
    /// that recovers composite components whose declared type disagrees with the element the expression
    /// actually selects, so the census checks it against itself rather than against upstream.
    /// </remarks>
    [Theory]
    [InlineData("Reference")]
    [InlineData("ResourceReference")]
    [InlineData("code")]
    [InlineData("codeOfT")]
    [InlineData("System.Code")]
    [InlineData("Coding")]
    [InlineData("CodeableConcept")]
    [InlineData("Identifier")]
    [InlineData("ContactPoint")]
    [InlineData("boolean")]
    [InlineData("id")]
    [InlineData("string")]
    [InlineData("HumanName")]
    [InlineData("Address")]
    [InlineData("markdown")]
    [InlineData("integer")]
    [InlineData("decimal")]
    [InlineData("date")]
    [InlineData("dateTime")]
    [InlineData("instant")]
    [InlineData("Period")]
    [InlineData("Timing")]
    [InlineData("Quantity")]
    [InlineData("Money")]
    [InlineData("Range")]
    [InlineData("uri")]
    [InlineData("url")]
    [InlineData("canonical")]
    [InlineData("oid")]
    [InlineData("CodeableReference")]
    public void GivenAnInferableFhirType_WhenCensused_ThenTheInferredSearchValueTypeHasAConverter(
        string fhirType)
    {
        SearchParamType? inferred = ElementSearchIndexer.InferSearchParamTypeFromFhirType(fhirType);

        inferred.ShouldNotBeNull(
            $"'{fhirType}' is listed here as inferable, so InferSearchParamTypeFromFhirType returning "
            + "null means the table lost a row. Restore the row or delete this case.");

        Type searchValueType = ElementSearchIndexer.GetSearchValueTypeForSearchParamType(inferred);

        Manager.TryGetConverter(fhirType, searchValueType, out _).ShouldBeTrue(
            $"InferSearchParamTypeFromFhirType maps '{fhirType}' to {inferred}, which demands a "
            + $"{searchValueType.Name}, and no converter produces one from '{fhirType}'. The inference "
            + "row is unreachable: every element it fires on is skipped as unsupported one line later.");
    }

    /// <summary>
    /// The inference table only ever fires on a converter-manager miss, so a type with no converter at
    /// all cannot be recovered by it. Recording the types the sweep actually reached keeps the reason
    /// those skips are correct attached to evidence rather than to a claim.
    /// </summary>
    [Theory]
    [InlineData("Attachment")]
    [InlineData("base64Binary")]
    [InlineData("Encounter.Location")]
    [InlineData("Location.Position")]
    public void GivenAnUnconvertibleFhirType_WhenCensused_ThenUpstreamCannotConvertItEither(
        string fhirType)
    {
        ElementSearchIndexer.InferSearchParamTypeFromFhirType(fhirType).ShouldBeNull(
            $"'{fhirType}' is recorded as unconvertible, so inference must not claim a type for it.");

        UpstreamConverterRegistrations.All
            .Any(registration => string.Equals(registration.FhirType, fhirType, StringComparison.Ordinal))
            .ShouldBeFalse(
                $"'{fhirType}' is recorded as unconvertible on the grounds that upstream cannot convert "
                + "it either, and the snapshot now says upstream can. Port the converter or correct the "
                + "record.");
    }

    private static IReadOnlyList<ConverterRegistration> Enumerate()
    {
        var schema = FhirVersion.R4.GetSchemaProvider();
        var referenceParser = new ReferenceSearchValueParser(schema, NullFhirBaseUriProvider.Instance);
        var elementResolver = new LightweightReferenceToElementResolver(referenceParser, schema);

        return SearchIndexerFactory.CreateConverters(schema, referenceParser, elementResolver)
            .SelectMany(converter => converter.FhirTypes.Select(fhirType =>
                new ConverterRegistration(
                    converter.GetType().Name,
                    fhirType,
                    converter.SearchValueType)))
            .ToArray();
    }

    private static string Render(IEnumerable<string> failures) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"""
             Converter registration census against microsoft/fhir-server
             {UpstreamConverterRegistrations.SourcePath}
             at {UpstreamConverterRegistrations.SourceCommit}:

             {string.Join(Environment.NewLine + Environment.NewLine, failures)}
             """);
}

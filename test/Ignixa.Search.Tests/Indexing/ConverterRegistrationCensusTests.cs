using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Tests.Indexing;

/// <summary>
/// A static census of what <c>FhirElementToSearchValueConverterManager</c> registers, against a
/// vendored snapshot of what <c>microsoft/fhir-server</c> registers.
/// </summary>
/// <remarks>
/// <para>
/// A differential corpus structurally cannot answer this question: the resource-backed parity harness
/// hands one converter manager, one definition manager and one set of indexer statics to both of its
/// indexers, so when both sides skip an element that is one object deciding once - a missing converter
/// and a correct skip look identical to it. That blind spot is how the <c>canonical</c> gap survived
/// until somebody switched a logger on, and why 115 recorded skips sat unadjudicated.
/// </para>
/// <para>
/// A registration census compares two independently authored sets rather than one set with itself, so it
/// answers the question for the whole class, fails when upstream adds a converter Ignixa lacks, and needs
/// nobody to notice a log line.
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

    /// <summary>
    /// Floor on the vendored snapshot's size, so an emptied table cannot satisfy every assertion below
    /// with nothing to compare against.
    /// </summary>
    /// <remarks>
    /// Not redundant with <see cref="UpstreamConverterRegistrations.ContentHash"/>: the hash is re-stated
    /// on every deliberate refresh, so it says nothing across refreshes. This floor survives them.
    /// </remarks>
    private const int MinimumUpstreamRegistrations = 47;

    /// <summary>
    /// Binds the snapshot's rows to the commit it claims to have been read from.
    /// </summary>
    /// <remarks>
    /// <see cref="UpstreamConverterRegistrations.SourceCommit"/> is otherwise only printed into failure
    /// messages, so a row added by hand - or a commit bumped without re-reading upstream - would change
    /// nothing any other assertion can see. Refreshing means changing both in the same edit.
    /// </remarks>
    [Fact]
    public void GivenTheSnapshot_WhenHashed_ThenItMatchesTheRecordedProvenance()
    {
        UpstreamConverterRegistrations.All.Count.ShouldBeGreaterThanOrEqualTo(
            MinimumUpstreamRegistrations,
            "The vendored upstream snapshot has fewer rows than the floor. Every comparison in this "
            + "census is against that table, so a shrinking table quietly shrinks the census. Raise the "
            + "floor when upstream genuinely grows; never lower it to accommodate a loss.");

        UpstreamConverterRegistrations.ComputeContentHash().ShouldBe(
            UpstreamConverterRegistrations.ContentHash,
            $"The vendored snapshot's rows no longer hash to the value recorded beside "
            + $"SourceCommit '{UpstreamConverterRegistrations.SourceCommit}'. If the rows were refreshed "
            + "from a newer upstream commit, update SourceCommit and ContentHash together. If they were "
            + "not, a row was edited without the provenance being restated - which is how a snapshot "
            + "stops describing the commit it names.");
    }

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
    /// recovering composite components whose declared type disagrees with the element selected, so the
    /// census checks it against itself rather than against upstream.
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
    /// <remarks>
    /// <c>Encounter.Location</c> was a case here until #454, when the element model stopped handing
    /// that backbone to a leaf-typed parameter. It is deliberately not carried as a historical row:
    /// the summary above promises these are types the sweep reached, and after #454 it does not reach
    /// this one - a row asserting inference and upstream registration would still pass while quietly
    /// falsifying that promise.
    /// </remarks>
    [Theory]
    [InlineData("Attachment")]
    [InlineData("base64Binary")]
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

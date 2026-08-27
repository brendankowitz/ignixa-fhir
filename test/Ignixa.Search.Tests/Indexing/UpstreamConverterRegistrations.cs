using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Search.Tests.Indexing;

/// <summary>
/// A snapshot of every <c>(FHIR type, search value type)</c> pair
/// <c>microsoft/fhir-server</c> registers in its converter manager.
/// </summary>
/// <remarks>
/// <para>
/// Provenance: <c>microsoft/fhir-server</c> at commit
/// <c>18e884cd5b53b8fbaa42706e134e5a3b591c44ce</c> (2026-08-25), read from
/// <c>src/Microsoft.Health.Fhir.Core/Features/Search/Converters</c>. One row per
/// <c>base(...)</c> FHIR type on each concrete
/// <c>FhirTypedElementToSearchValueConverter&lt;T&gt;</c>, which is exactly what upstream's
/// <c>FhirTypedElementToSearchValueConverterManager</c> keys its dictionary by. Ignixa's
/// <c>FhirElementToSearchValueConverterManager</c> keys the same way, so the two sets are
/// directly comparable.
/// </para>
/// <para>
/// This is a vendored snapshot rather than a live fetch on purpose: the census has to be able to
/// fail in CI without network access, and a table that refreshed itself could never report
/// "upstream added a converter Ignixa lacks" - it would silently absorb the addition. Refresh it
/// deliberately, and expect <see cref="KnownConverterDivergences"/> to need an entry or Ignixa to
/// need a converter when you do.
/// </para>
/// <para>
/// The search value types are written as <c>typeof</c> against Ignixa's own types rather than as
/// strings, so a rename on either side is a compile error instead of a silently unmatched row.
/// Upstream's <c>CompositeSearchValue</c> has no row here because no converter produces one;
/// composites are assembled by the indexer from component values.
/// </para>
/// </remarks>
internal static class UpstreamConverterRegistrations
{
    public const string SourceCommit = "18e884cd5b53b8fbaa42706e134e5a3b591c44ce";

    public const string SourcePath =
        "microsoft/fhir-server src/Microsoft.Health.Fhir.Core/Features/Search/Converters";

    /// <summary>
    /// SHA-256 over <see cref="All"/>, so no row can be added, removed or edited without
    /// <see cref="SourceCommit"/> being restated in the same change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this, <see cref="SourceCommit"/> is read only to be printed into failure messages -
    /// nothing binds it to the rows. The rest of the census catches an emptied snapshot, a deleted row and
    /// an added row for a pair Ignixa lacks, but it compares <em>sets of pairs</em>: a duplicate row, or a
    /// row whose converter name was edited, changes nothing it can see. This hash makes any such edit
    /// impossible without also editing the line beside <see cref="SourceCommit"/>. What it cannot do is
    /// verify the commit itself - a <see cref="SourceCommit"/> bumped while the rows stay put is not
    /// checkable offline by any means, which is why refreshing is described as one edit below.
    /// </para>
    /// <para>
    /// To refresh: re-read the converters at the new upstream commit, update the rows, run
    /// <c>ConverterRegistrationCensusTests.GivenTheSnapshot_WhenHashed_ThenItMatchesTheRecordedProvenance</c>
    /// and paste the hash it reports here together with the new <see cref="SourceCommit"/>. Updating the
    /// hash without updating the commit is the mistake this exists to make visible.
    /// </para>
    /// </remarks>
    public const string ContentHash = "6ea7f645cb67673df1e81e0bb823a0464c0c6e60f76f7a7f6e900f651219c9a3";

    public static IReadOnlyList<ConverterRegistration> All { get; } =
    [
        new("AddressToStringSearchValueConverter", "Address", typeof(StringSearchValue)),
        new("BooleanToTokenSearchValueConverter", "boolean", typeof(TokenSearchValue)),
        new("BooleanToTokenSearchValueConverter", "System.Boolean", typeof(TokenSearchValue)),
        new("CanonicalToReferenceSearchValueConverter", "canonical", typeof(ReferenceSearchValue)),
        new("CanonicalToUriSearchValueConverter", "canonical", typeof(UriSearchValue)),
        new("CodeToTokenSearchValueConverter", "code", typeof(TokenSearchValue)),
        new("CodeToTokenSearchValueConverter", "codeOfT", typeof(TokenSearchValue)),
        new("CodeToTokenSearchValueConverter", "System.Code", typeof(TokenSearchValue)),
        new("CodeableConceptToTokenSearchValueConverter", "CodeableConcept", typeof(TokenSearchValue)),
        new("CodeableReferenceToReferenceSearchValueConverter", "CodeableReference", typeof(ReferenceSearchValue)),
        new("CodeableReferenceToTokenSearchValueConverter", "CodeableReference", typeof(TokenSearchValue)),
        new("CodingToTokenSearchValueConverter", "Coding", typeof(TokenSearchValue)),
        new("ContactPointToTokenSearchValueConverter", "ContactPoint", typeof(TokenSearchValue)),
        new("DateToDateTimeSearchValueConverter", "date", typeof(DateTimeSearchValue)),
        new("DateToDateTimeSearchValueConverter", "dateTime", typeof(DateTimeSearchValue)),
        new("DateToDateTimeSearchValueConverter", "System.DateTime", typeof(DateTimeSearchValue)),
        new("DateToDateTimeSearchValueConverter", "System.Date", typeof(DateTimeSearchValue)),
        new("DecimalToNumberSearchValueConverter", "decimal", typeof(NumberSearchValue)),
        new("DecimalToNumberSearchValueConverter", "System.Decimal", typeof(NumberSearchValue)),
        new("HumanNameToStringSearchValueConverter", "HumanName", typeof(StringSearchValue)),
        new("IdToReferenceSearchValueConverter", "id", typeof(ReferenceSearchValue)),
        new("IdToTokenSearchValueConverter", "id", typeof(TokenSearchValue)),
        new("IdentifierToStringSearchValueConverter", "Identifier", typeof(StringSearchValue)),
        new("IdentifierToTokenSearchValueConverter", "Identifier", typeof(TokenSearchValue)),
        new("InstantToDateTimeSearchValueConverter", "instant", typeof(DateTimeSearchValue)),
        new("IntegerToNumberSearchValueConverter", "integer", typeof(NumberSearchValue)),
        new("IntegerToNumberSearchValueConverter", "positiveInt", typeof(NumberSearchValue)),
        new("IntegerToNumberSearchValueConverter", "unsignedInt", typeof(NumberSearchValue)),
        new("IntegerToNumberSearchValueConverter", "System.Integer", typeof(NumberSearchValue)),
        new("MarkdownToStringSearchValueConverter", "markdown", typeof(StringSearchValue)),
        new("MoneyToQuantitySearchValueConverter", "Money", typeof(QuantitySearchValue)),
        new("OidToUriSearchValueConverter", "oid", typeof(UriSearchValue)),
        new("PeriodToDateTimeSearchValueConverter", "Period", typeof(DateTimeSearchValue)),
        new("QuantityToQuantitySearchValueConverter", "Quantity", typeof(QuantitySearchValue)),
        new("QuantityToQuantitySearchValueConverter", "System.Quantity", typeof(QuantitySearchValue)),
        new("RangeToNumberSearchValueConverter", "Range", typeof(NumberSearchValue)),
        new("RangeToQuantitySearchValueConverter", "Range", typeof(QuantitySearchValue)),
        new("ReferenceToUriSearchValueConverter", "Reference", typeof(UriSearchValue)),
        new("ResourceReferenceToReferenceSearchValueConverter", "Reference", typeof(ReferenceSearchValue)),
        new("StringToStringSearchValueConverter", "string", typeof(StringSearchValue)),
        new("StringToStringSearchValueConverter", "System.String", typeof(StringSearchValue)),
        new("StringToTokenSearchValueConverter", "string", typeof(TokenSearchValue)),
        new("StringToTokenSearchValueConverter", "System.String", typeof(TokenSearchValue)),
        new("UriToReferenceSearchValueConverter", "uri", typeof(ReferenceSearchValue)),
        new("UriToReferenceSearchValueConverter", "url", typeof(ReferenceSearchValue)),
        new("UriToUriSearchValueConverter", "uri", typeof(UriSearchValue)),
        new("UriToUriSearchValueConverter", "url", typeof(UriSearchValue)),
    ];

    public static IReadOnlySet<ConverterPair> Pairs { get; } =
        All.Select(registration => registration.Pair).ToHashSet();

    /// <summary>
    /// The hash <see cref="ContentHash"/> is expected to hold: SHA-256, lower-case hex, over the rows
    /// rendered one per line in ordinal order, so the value does not depend on declaration order.
    /// </summary>
    public static string ComputeContentHash()
    {
        string rendered = string.Join(
            '\n',
            All
                .Select(registration => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{registration.ConverterName}|{registration.FhirType}|{registration.SearchValueType.Name}"))
                .Order(StringComparer.Ordinal));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rendered)));
    }
}

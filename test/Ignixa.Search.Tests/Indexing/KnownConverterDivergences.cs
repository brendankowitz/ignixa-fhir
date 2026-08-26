using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Search.Tests.Indexing;

/// <summary>
/// Every place Ignixa's converter registrations deliberately differ from
/// <see cref="UpstreamConverterRegistrations"/>, with the reason.
/// </summary>
/// <remarks>
/// <para>
/// This table is the point of the census. A census that simply demanded parity with upstream would
/// fail on storage decisions this codebase has taken deliberately, and the only way to make it pass
/// would be to adopt a storage model Ignixa does not use. So every difference is allowed - once
/// somebody writes down which it is.
/// </para>
/// <para>
/// The table cannot lie in either direction. <see cref="ConverterRegistrationCensusTests"/> fails if
/// a pair listed in <see cref="MissingFromIgnixa"/> is actually registered (the divergence closed and
/// the entry is stale), if a pair listed in <see cref="AdditionalInIgnixa"/> is not registered or is
/// present upstream too (the entry describes something that is not a divergence), and if any pair on
/// either side is absent from both the snapshot and this table.
/// </para>
/// </remarks>
internal static class KnownConverterDivergences
{
    /// <summary>
    /// Pairs upstream registers and Ignixa does not.
    /// </summary>
    public static IReadOnlyDictionary<ConverterPair, string> MissingFromIgnixa { get; } =
        new Dictionary<ConverterPair, string>
        {
            [new("canonical", typeof(ReferenceSearchValue))] =
                """
                Deliberate storage divergence, tracked as #430. Ignixa stores canonical references in
                UriSearchParam rather than upstream's ReferenceSearchParam, so CanonicalToUri is the
                registration Ignixa keeps and CanonicalToReference is the one it does not port. The
                consequence is real and measured, not theoretical: 46 shipped Reference-typed search
                parameters currently index nothing, which is why #430 is release-blocking. Closing it
                is a decision about which table canonical lands in, not about restoring parity here -
                if it is closed by adopting upstream's converter, delete this entry.
                """,
            [new("Identifier", typeof(StringSearchValue))] =
                """
                Deliberate divergence, tracked as #421. Upstream's IdentifierToStringSearchValueConverter
                exists to serve the ':identifier' reference modifier by flattening an Identifier into a
                string. Ignixa implements ':identifier' with a derived token search parameter instead,
                which keeps system and value separable at query time rather than concatenated into one
                string. Registering upstream's converter would add a second, weaker representation of
                the same data.
                """,
            [new("id", typeof(ReferenceSearchValue))] =
                """
                Not ported, and no shipped search parameter reaches it. Upstream's
                IdToReferenceSearchValueConverter turns a bare 'id' primitive into a reference for
                search parameters whose expression selects Resource.id under a Reference-typed
                parameter. No skip attributable to (id -> ReferenceSearchValue) appears in the
                resource-backed parity sweep across STU3, R4, R4B, R5 and R6, so this is an unreached
                registration rather than a measured gap. Port it if a parameter is found that needs it.
                """,
            [new("Reference", typeof(UriSearchValue))] =
                """
                Not ported, and no shipped search parameter reaches it. Upstream's
                ReferenceToUriSearchValueConverter indexes a Reference under a Uri-typed parameter.
                Ignixa registers the opposite direction (uri -> ReferenceSearchValue) which is what the
                shipped parameters actually use. No skip attributable to
                (Reference -> UriSearchValue) appears in the resource-backed parity sweep.
                """,
        };

    /// <summary>
    /// Pairs Ignixa registers and upstream does not.
    /// </summary>
    public static IReadOnlyDictionary<ConverterPair, string> AdditionalInIgnixa { get; } =
        new Dictionary<ConverterPair, string>
        {
            [new("ResourceReference", typeof(ReferenceSearchValue))] =
                """
                Ignixa-only, and required by Ignixa's own inference table rather than by upstream.
                InferSearchParamTypeFromFhirType maps both 'Reference' and 'ResourceReference' to
                SearchParamType.Reference, and ElementSearchIndexer's STU3 target-type filter matches
                both spellings, so the converter registering only 'Reference' made the inference row
                unreachable. Upstream has no inference table and no need for the synonym.
                """,
            [new("Timing", typeof(DateTimeSearchValue))] =
                """
                Ignixa-only, and an improvement rather than a drift. Several shipped date parameters
                select a Timing - AdverseEvent.occurrence.ofType(Timing) and
                CarePlan.activity.detail.scheduled among them - and upstream has no Timing converter, so
                those elements are skipped there. TimingToDateTimeSearchValueConverter indexes the
                Timing's event instants, so the parameter matches.
                """,
        };
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Abstractions.Terminology;

namespace Ignixa.Validation.Services.Terminology;

internal sealed class SourceRouter
{
    private readonly IReadOnlyList<ITerminologySource> _sources;
    private readonly IReadOnlyList<ILegacyValueSetOperations> _legacy;

    public SourceRouter(IReadOnlyList<ITerminologySource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources;
        _legacy = sources.OfType<ILegacyValueSetOperations>().ToList();
    }

    public ILegacyValueSetOperations FirstLegacy =>
        _legacy.Count > 0 ? _legacy[0] : throw NoLegacySource();

    public async Task<RoutedAnswer<T>> AskAsync<T>(
        CanonicalKind kind,
        CanonicalRef canonical,
        Func<ITerminologySource, CancellationToken, Task<T>> ask,
        Func<UnknownReason, T> undetermined,
        CancellationToken cancellationToken)
        where T : ITerminologyAnswer
    {
        var partial = new List<ITerminologySource>();
        foreach (var source in _sources)
        {
            var coverage = await source.GetCoverageAsync(kind, canonical, cancellationToken);
            if (coverage == SourceCoverage.Authoritative)
            {
                var answer = await ask(source, cancellationToken);
                return new RoutedAnswer<T>(answer, answer.Unknown is null ? [] : [answer.Unknown]);
            }

            if (coverage == SourceCoverage.Partial)
            {
                partial.Add(source);
            }
        }

        return partial.Count == 0
            ? Uncovered(kind, canonical, undetermined)
            : await AskPartialAsync(partial, ask, cancellationToken);
    }

    public async Task<ILegacyValueSetOperations> SelectLegacyAsync(CanonicalRef valueSet, CancellationToken cancellationToken)
    {
        if (_legacy.Count == 0)
        {
            throw NoLegacySource();
        }

        foreach (var legacy in _legacy)
        {
            var coverage = await ((ITerminologySource)legacy).GetCoverageAsync(CanonicalKind.ValueSet, valueSet, cancellationToken);
            if (coverage == SourceCoverage.Authoritative)
            {
                return legacy;
            }
        }

        return _legacy[^1];
    }

    private static async Task<RoutedAnswer<T>> AskPartialAsync<T>(
        List<ITerminologySource> partial,
        Func<ITerminologySource, CancellationToken, Task<T>> ask,
        CancellationToken cancellationToken)
        where T : ITerminologyAnswer
    {
        var unknowns = new List<UnknownReason>();
        T? first = default;
        foreach (var source in partial)
        {
            var answer = await ask(source, cancellationToken);
            if (answer.Unknown is null)
            {
                return new RoutedAnswer<T>(answer, []);
            }

            first ??= answer;
            unknowns.Add(answer.Unknown);
        }

        return new RoutedAnswer<T>(first!, unknowns);
    }

    private static RoutedAnswer<T> Uncovered<T>(CanonicalKind kind, CanonicalRef canonical, Func<UnknownReason, T> undetermined)
        where T : ITerminologyAnswer
    {
        var reason = kind == CanonicalKind.ValueSet
            ? new UnknownReason(UnknownKind.ValueSetNotFound, canonical.ToString(), $"No terminology source holds ValueSet '{canonical}'")
            : new UnknownReason(UnknownKind.SystemNotAvailable, canonical.ToString(), $"No terminology source holds {kind} '{canonical}'");
        return new RoutedAnswer<T>(undetermined(reason), [reason]);
    }

    private static InvalidOperationException NoLegacySource() =>
        new("No terminology source implements ILegacyValueSetOperations; at least one is required until Phase 5.");
}

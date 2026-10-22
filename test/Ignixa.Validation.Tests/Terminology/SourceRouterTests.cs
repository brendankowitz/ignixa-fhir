// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Abstractions.Terminology;
using Ignixa.Validation.Services.Terminology;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Terminology;

public class SourceRouterTests
{
    private static readonly CanonicalRef System = new("http://example.org/cs");
    private static readonly UnknownReason ReasonA = new(UnknownKind.NonEnumerableSystem, "http://example.org/cs", "a");
    private static readonly UnknownReason ReasonB = new(UnknownKind.SystemNotAvailable, "http://example.org/cs", "b");

    private static ITerminologySource Source(string name, SourceCoverage coverage, MembershipResult answer)
    {
        var source = Substitute.For<ITerminologySource, ILegacyValueSetOperations>();
        source.Name.Returns(name);
#pragma warning disable CA2012
        source.GetCoverageAsync(Arg.Any<CanonicalKind>(), Arg.Any<CanonicalRef>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<SourceCoverage>(coverage));
#pragma warning restore CA2012
        Answers[source] = answer;
        return source;
    }

    private static readonly Dictionary<ITerminologySource, MembershipResult> Answers = new(ReferenceEqualityComparer.Instance);

    private static Task<RoutedAnswer<MembershipResult>> AskAsync(SourceRouter router, List<string> asked) =>
        router.AskAsync(
            CanonicalKind.CodeSystem,
            System,
            (source, _) =>
            {
                asked.Add(source.Name);
                return Task.FromResult(Answers[source]);
            },
            MembershipResult.Undetermined,
            CancellationToken.None);

    [Fact]
    public async Task GivenAuthoritativeSourceAfterPartial_WhenAsked_ThenOnlyAuthoritativeAnswers()
    {
        var asked = new List<string>();
        var router = new SourceRouter([
            Source("package", SourceCoverage.Partial, MembershipResult.Member),
            Source("sql", SourceCoverage.Authoritative, MembershipResult.NotMember)]);

        var routed = await AskAsync(router, asked);

        routed.Answer.ShouldBe(MembershipResult.NotMember);
        routed.Unknowns.ShouldBeEmpty();
        asked.ShouldBe(["sql"]);
    }

    [Fact]
    public async Task GivenPartialUnknownThenPartialFalse_WhenAsked_ThenFirstDeterminedAnswerWins()
    {
        var asked = new List<string>();
        var router = new SourceRouter([
            Source("first", SourceCoverage.Partial, MembershipResult.Undetermined(ReasonA)),
            Source("second", SourceCoverage.Partial, MembershipResult.NotMember)]);

        var routed = await AskAsync(router, asked);

        routed.Answer.Result.ShouldBe(Determination.False);
        routed.Unknowns.ShouldBeEmpty();
        asked.ShouldBe(["first", "second"]);
    }

    [Fact]
    public async Task GivenEveryPartialUnknown_WhenAsked_ThenFirstAnswerReturnedWithAllReasons()
    {
        var router = new SourceRouter([
            Source("first", SourceCoverage.Partial, MembershipResult.Undetermined(ReasonA)),
            Source("second", SourceCoverage.Partial, MembershipResult.Undetermined(ReasonB))]);

        var routed = await AskAsync(router, []);

        routed.Answer.Unknown.ShouldBe(ReasonA);
        routed.Unknowns.ShouldBe([ReasonA, ReasonB]);
    }

    [Fact]
    public async Task GivenNoCoveringSource_WhenAsked_ThenSystemNotAvailableForTheCanonical()
    {
        var asked = new List<string>();
        var router = new SourceRouter([Source("sql", SourceCoverage.None, MembershipResult.Member)]);

        var routed = await AskAsync(router, asked);

        routed.Answer.Result.ShouldBe(Determination.Unknown);
        routed.Answer.Unknown!.Kind.ShouldBe(UnknownKind.SystemNotAvailable);
        routed.Answer.Unknown.Subject.ShouldBe("http://example.org/cs");
        asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenSourceThrows_WhenAsked_ThenExceptionPropagates()
    {
        var router = new SourceRouter([Source("sql", SourceCoverage.Authoritative, MembershipResult.Member)]);

        await Should.ThrowAsync<InvalidOperationException>(() => router.AskAsync<MembershipResult>(
            CanonicalKind.CodeSystem,
            System,
            (_, _) => throw new InvalidOperationException("db down"),
            MembershipResult.Undetermined,
            CancellationToken.None));
    }

    [Fact]
    public async Task GivenAuthoritativeValueSetSource_WhenSelectingLegacy_ThenItIsChosen()
    {
        var sql = Source("sql", SourceCoverage.Authoritative, MembershipResult.Member);
        var package = Source("package", SourceCoverage.Authoritative, MembershipResult.Member);

        var chosen = await new SourceRouter([sql, package]).SelectLegacyAsync(new CanonicalRef("http://vs|1"), CancellationToken.None);

        chosen.ShouldBeSameAs(sql);
        await sql.Received(1).GetCoverageAsync(CanonicalKind.ValueSet, new CanonicalRef("http://vs|1"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenNoAuthoritativeValueSetSource_WhenSelectingLegacy_ThenLastLegacySourceIsTheFallback()
    {
        var sql = Source("sql", SourceCoverage.None, MembershipResult.Member);
        var package = Source("package", SourceCoverage.None, MembershipResult.Member);

        var chosen = await new SourceRouter([sql, package]).SelectLegacyAsync(new CanonicalRef("http://vs"), CancellationToken.None);

        chosen.ShouldBeSameAs(package);
    }

    [Fact]
    public void GivenSources_WhenAskingForFirstLegacy_ThenRegistrationOrderWins()
    {
        var sql = Source("sql", SourceCoverage.None, MembershipResult.Member);

        new SourceRouter([sql, Source("package", SourceCoverage.None, MembershipResult.Member)]).FirstLegacy.ShouldBeSameAs(sql);
    }

    [Fact]
    public async Task GivenNoLegacyCapableSource_WhenSelectingLegacy_ThenThrows()
    {
        var plain = Substitute.For<ITerminologySource>();

        await Should.ThrowAsync<InvalidOperationException>(() => new SourceRouter([plain]).SelectLegacyAsync(new CanonicalRef("http://vs"), CancellationToken.None));
    }
}

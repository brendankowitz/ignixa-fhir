// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Validation.Services.Terminology;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Terminology;

/// <summary>
/// Pins the precedence introduced with the layered provider in a6f5747c and kept at merge-base 9db0620d:
/// additional (IG) providers are consulted before the primary (base-spec) provider, so an IG ValueSet that
/// shares a canonical with a base-spec one overrides it.
/// </summary>
public class LayeredValueSetProviderTests
{
    private const string ValueSet = "http://example.org/vs";

    private readonly IValueSetProvider _primary = Substitute.For<IValueSetProvider>();
    private readonly IValueSetProvider _additional = Substitute.For<IValueSetProvider>();

    private LayeredValueSetProvider Create() => new(_primary, [_additional]);

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void GivenBothProvidersAnswer_WhenValidatingCode_ThenAdditionalAnswerWins(bool additional, bool primary)
    {
        // Querying the primary first would let a base-spec ValueSet override an IG ValueSet with the same canonical.
        _additional.IsValidCode(ValueSet, "x").Returns(additional);
        _primary.IsValidCode(ValueSet, "x").Returns(primary);

        Create().IsValidCode(ValueSet, "x").ShouldBe(additional);
        _primary.DidNotReceiveWithAnyArgs().IsValidCode(default!, default!);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void GivenAdditionalDoesNotKnowValueSet_WhenValidatingCode_ThenPrimaryAnswers(bool? primary)
    {
        _additional.IsValidCode(ValueSet, "x").Returns((bool?)null);
        _primary.IsValidCode(ValueSet, "x").Returns(primary);

        Create().IsValidCode(ValueSet, "x").ShouldBe(primary);
    }

    [Fact]
    public void GivenBothProvidersHoldCodes_WhenGettingCodes_ThenAdditionalCodesWin()
    {
        IReadOnlyList<FhirCode> igCodes = [new FhirCode("http://example.org/cs", "ig", "IG")];
        _additional.GetCodes(ValueSet).Returns(igCodes);
        _primary.GetCodes(ValueSet).Returns([new FhirCode("http://example.org/cs", "base", "Base")]);

        Create().GetCodes(ValueSet).ShouldBeSameAs(igCodes);
    }

    [Fact]
    public void GivenAdditionalDoesNotHoldCodes_WhenGettingCodes_ThenPrimaryCodesAreReturned()
    {
        IReadOnlyList<FhirCode> baseCodes = [new FhirCode("http://example.org/cs", "base", "Base")];
        _additional.GetCodes(ValueSet).Returns((IReadOnlyList<FhirCode>?)null);
        _primary.GetCodes(ValueSet).Returns(baseCodes);

        Create().GetCodes(ValueSet).ShouldBeSameAs(baseCodes);
    }
}

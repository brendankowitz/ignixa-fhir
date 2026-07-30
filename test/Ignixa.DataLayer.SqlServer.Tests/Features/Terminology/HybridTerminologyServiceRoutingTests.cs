// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlServer.Features.Terminology;
using Ignixa.Domain.Terminology;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Ignixa.DataLayer.SqlServer.Tests.Features.Terminology;

/// <summary>
/// Pins <see cref="HybridTerminologyService"/>'s routing decision: which of its two
/// <see cref="ITerminologyService"/> dependencies each operation reaches, given the import status of the
/// canonical involved. Every test asserts both halves -- the side that must be called and the side that must
/// not -- because a decorator that calls both is indistinguishable from one that routes correctly if only
/// the result is checked.
/// </summary>
public class HybridTerminologyServiceRoutingTests
{
    private const string CodeSystemUrl = "http://example.org/cs";
    private const string ValueSetUrl = "http://example.org/vs";

    private readonly ITerminologyService _sql = Substitute.For<ITerminologyService>();
    private readonly ITerminologyService _fallback = Substitute.For<ITerminologyService>();
    private readonly ITerminologyImportStatusProvider _importStatus =
        Substitute.For<ITerminologyImportStatusProvider>();

    private HybridTerminologyService CreateHybrid() =>
        new(_sql, _importStatus, _fallback, NullLogger<HybridTerminologyService>.Instance);

    private void GivenImportStatus(string canonical, TerminologyImportStatus? status) =>
        _importStatus.GetImportStatusAsync(canonical, Arg.Any<CancellationToken>()).Returns(status);

    [Fact]
    public async Task GivenAnImportedCodeSystem_WhenLookingUpACode_ThenTheSqlServiceIsUsed()
    {
        // Arrange
        GivenImportStatus(CodeSystemUrl, TerminologyImportStatus.Completed);
        _sql.LookupCodeAsync(CodeSystemUrl, "abc", null, Arg.Any<CancellationToken>())
            .Returns(new LookupResult(true, null, null, "From SQL", null, null, null));

        // Act
        var result = await CreateHybrid().LookupCodeAsync(CodeSystemUrl, "abc", null, CancellationToken.None);

        // Assert
        result.Display.ShouldBe("From SQL");
        await _sql.Received(1).LookupCodeAsync(CodeSystemUrl, "abc", null, Arg.Any<CancellationToken>());
        await _fallback.DidNotReceiveWithAnyArgs().LookupCodeAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task GivenAnUnknownCodeSystem_WhenLookingUpACode_ThenTheFallbackServiceIsUsed()
    {
        // Arrange
        GivenImportStatus(CodeSystemUrl, null);
        _fallback.LookupCodeAsync(CodeSystemUrl, "abc", null, Arg.Any<CancellationToken>())
            .Returns(new LookupResult(true, null, null, "From fallback", null, null, null));

        // Act
        var result = await CreateHybrid().LookupCodeAsync(CodeSystemUrl, "abc", null, CancellationToken.None);

        // Assert
        result.Display.ShouldBe("From fallback");
        await _fallback.Received(1).LookupCodeAsync(CodeSystemUrl, "abc", null, Arg.Any<CancellationToken>());
        await _sql.DidNotReceiveWithAnyArgs().LookupCodeAsync(default!, default!, default, default);
    }

    /// <summary>
    /// Only <see cref="TerminologyImportStatus.Completed"/> routes to SQL. A half-finished import must not:
    /// the terminology tables are populated incrementally, so <c>InProgress</c> means "some rows", which
    /// would answer "code not found" for codes that are merely not loaded yet.
    /// </summary>
    [Theory]
    [InlineData(TerminologyImportStatus.NotApplicable)]
    [InlineData(TerminologyImportStatus.Pending)]
    [InlineData(TerminologyImportStatus.InProgress)]
    [InlineData(TerminologyImportStatus.Failed)]
    [InlineData(TerminologyImportStatus.Skipped)]
    public async Task GivenACodeSystemThatIsNotFullyImported_WhenLookingUpACode_ThenTheFallbackServiceIsUsed(
        TerminologyImportStatus status)
    {
        // Arrange
        GivenImportStatus(CodeSystemUrl, status);
        _fallback.LookupCodeAsync(CodeSystemUrl, "abc", null, Arg.Any<CancellationToken>())
            .Returns(new LookupResult(true, null, null, "From fallback", null, null, null));

        // Act
        var result = await CreateHybrid().LookupCodeAsync(CodeSystemUrl, "abc", null, CancellationToken.None);

        // Assert
        result.Display.ShouldBe("From fallback");
        await _fallback.Received(1).LookupCodeAsync(CodeSystemUrl, "abc", null, Arg.Any<CancellationToken>());
        await _sql.DidNotReceiveWithAnyArgs().LookupCodeAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task GivenAnImportedValueSet_WhenExpanding_ThenTheSqlServiceIsUsed()
    {
        // Arrange
        var parameters = new ExpansionParameters(ValueSetUrl);
        GivenImportStatus(ValueSetUrl, TerminologyImportStatus.Completed);
        _sql.ExpandValueSetAsync(parameters, Arg.Any<CancellationToken>())
            .Returns(new ExpandResult("sql", DateTimeOffset.UnixEpoch, 1, 0, []));

        // Act
        var result = await CreateHybrid().ExpandValueSetAsync(parameters, CancellationToken.None);

        // Assert
        result!.Identifier.ShouldBe("sql");
        await _sql.Received(1).ExpandValueSetAsync(parameters, Arg.Any<CancellationToken>());
        await _fallback.DidNotReceiveWithAnyArgs().ExpandValueSetAsync(default!, default);
    }

    [Fact]
    public async Task GivenAnUnimportedValueSet_WhenExpanding_ThenTheFallbackServiceIsUsed()
    {
        // Arrange
        var parameters = new ExpansionParameters(ValueSetUrl);
        GivenImportStatus(ValueSetUrl, TerminologyImportStatus.Pending);
        _fallback.ExpandValueSetAsync(parameters, Arg.Any<CancellationToken>())
            .Returns(new ExpandResult("fallback", DateTimeOffset.UnixEpoch, 1, 0, []));

        // Act
        var result = await CreateHybrid().ExpandValueSetAsync(parameters, CancellationToken.None);

        // Assert
        result!.Identifier.ShouldBe("fallback");
        await _fallback.Received(1).ExpandValueSetAsync(parameters, Arg.Any<CancellationToken>());
        await _sql.DidNotReceiveWithAnyArgs().ExpandValueSetAsync(default!, default);
    }

    [Fact]
    public async Task GivenAnImportedValueSet_WhenValidatingACode_ThenTheSqlServiceIsUsed()
    {
        // Arrange
        GivenImportStatus(ValueSetUrl, TerminologyImportStatus.Completed);
        _sql.ValidateCodeAsync("http://example.org", "abc", null, ValueSetUrl, Arg.Any<CancellationToken>())
            .Returns(new TerminologyValidationResult(true, IssueSeverity.Information, "sql"));

        // Act
        var result = await CreateHybrid().ValidateCodeAsync(
            "http://example.org", "abc", null, ValueSetUrl, CancellationToken.None);

        // Assert
        result.Message.ShouldBe("sql");
        await _sql.Received(1).ValidateCodeAsync(
            "http://example.org", "abc", null, ValueSetUrl, Arg.Any<CancellationToken>());
        await _fallback.DidNotReceiveWithAnyArgs().ValidateCodeAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task GivenAnUnimportedValueSet_WhenValidatingACode_ThenTheFallbackServiceIsUsed()
    {
        // Arrange
        GivenImportStatus(ValueSetUrl, null);
        _fallback.ValidateCodeAsync("http://example.org", "abc", null, ValueSetUrl, Arg.Any<CancellationToken>())
            .Returns(new TerminologyValidationResult(true, IssueSeverity.Information, "fallback"));

        // Act
        var result = await CreateHybrid().ValidateCodeAsync(
            "http://example.org", "abc", null, ValueSetUrl, CancellationToken.None);

        // Assert
        result.Message.ShouldBe("fallback");
        await _fallback.Received(1).ValidateCodeAsync(
            "http://example.org", "abc", null, ValueSetUrl, Arg.Any<CancellationToken>());
        await _sql.DidNotReceiveWithAnyArgs().ValidateCodeAsync(default, default, default, default, default);
    }

    /// <summary>
    /// Without a ValueSet there is no canonical to ask about, so the routing decision is never made and
    /// neither service is consulted.
    /// </summary>
    [Fact]
    public async Task GivenNoValueSetUrl_WhenValidatingACode_ThenNeitherServiceIsConsulted()
    {
        // Act
        var result = await CreateHybrid().ValidateCodeAsync(
            "http://example.org", "abc", null, valueSetUrl: null, CancellationToken.None);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Severity.ShouldBe(IssueSeverity.Error);
        await _importStatus.DidNotReceiveWithAnyArgs().GetImportStatusAsync(default!, default);
        await _sql.DidNotReceiveWithAnyArgs().ValidateCodeAsync(default, default, default, default, default);
        await _fallback.DidNotReceiveWithAnyArgs().ValidateCodeAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task GivenAnImportedValueSet_WhenValidatingABinding_ThenTheSqlServiceIsUsed()
    {
        // Arrange
        GivenImportStatus(ValueSetUrl, TerminologyImportStatus.Completed);
        _sql.ValidateBindingAsync(
                ValueSetUrl, BindingStrength.Required, "http://example.org", "abc", "Display", null,
                Arg.Any<CancellationToken>())
            .Returns(new BindingValidationResult(true, BindingStrength.Required, IssueSeverity.Information, "sql", null));

        // Act
        var result = await CreateHybrid().ValidateBindingAsync(
            ValueSetUrl, BindingStrength.Required, "http://example.org", "abc", "Display", null,
            CancellationToken.None);

        // Assert
        result.Message.ShouldBe("sql");
        await _sql.Received(1).ValidateBindingAsync(
            ValueSetUrl, BindingStrength.Required, "http://example.org", "abc", "Display", null,
            Arg.Any<CancellationToken>());
        await _fallback.DidNotReceiveWithAnyArgs()
            .ValidateBindingAsync(default!, default, default, default, default, default, default);
    }

    [Fact]
    public async Task GivenAnUnimportedValueSet_WhenValidatingABinding_ThenTheFallbackServiceIsUsed()
    {
        // Arrange
        GivenImportStatus(ValueSetUrl, TerminologyImportStatus.Failed);
        _fallback.ValidateBindingAsync(
                ValueSetUrl, BindingStrength.Required, "http://example.org", "abc", "Display", null,
                Arg.Any<CancellationToken>())
            .Returns(new BindingValidationResult(
                true, BindingStrength.Required, IssueSeverity.Information, "fallback", null));

        // Act
        var result = await CreateHybrid().ValidateBindingAsync(
            ValueSetUrl, BindingStrength.Required, "http://example.org", "abc", "Display", null,
            CancellationToken.None);

        // Assert
        result.Message.ShouldBe("fallback");
        await _fallback.Received(1).ValidateBindingAsync(
            ValueSetUrl, BindingStrength.Required, "http://example.org", "abc", "Display", null,
            Arg.Any<CancellationToken>());
        await _sql.DidNotReceiveWithAnyArgs()
            .ValidateBindingAsync(default!, default, default, default, default, default, default);
    }

    /// <summary>
    /// $translate has no in-memory equivalent -- the fallback holds no ConceptMaps -- so it goes to SQL
    /// unconditionally and does not pay for an import-status query first.
    /// </summary>
    [Fact]
    public async Task GivenAnyImportState_WhenTranslating_ThenTheSqlServiceIsUsedWithoutAStatusQuery()
    {
        // Arrange
        var parameters = new TranslateParameters(
            null, null, "abc", CodeSystemUrl, null, null, null, null);
        _sql.TranslateCodeAsync(parameters, Arg.Any<CancellationToken>())
            .Returns(new TranslateResult(true, "sql", []));

        // Act
        var result = await CreateHybrid().TranslateCodeAsync(parameters, CancellationToken.None);

        // Assert
        result.Message.ShouldBe("sql");
        await _sql.Received(1).TranslateCodeAsync(parameters, Arg.Any<CancellationToken>());
        await _fallback.DidNotReceiveWithAnyArgs().TranslateCodeAsync(default!, default);
        await _importStatus.DidNotReceiveWithAnyArgs().GetImportStatusAsync(default!, default);
    }

    /// <summary>
    /// $subsumes needs the concept hierarchy, which only the SQL tables carry, so it too is unconditional.
    /// </summary>
    [Fact]
    public async Task GivenAnyImportState_WhenTestingSubsumption_ThenTheSqlServiceIsUsedWithoutAStatusQuery()
    {
        // Arrange
        var parameters = new SubsumesParameters("a", "b", CodeSystemUrl, null);
        _sql.SubsumesAsync(parameters, Arg.Any<CancellationToken>()).Returns(new SubsumesResult("subsumes"));

        // Act
        var result = await CreateHybrid().SubsumesAsync(parameters, CancellationToken.None);

        // Assert
        result.Outcome.ShouldBe("subsumes");
        await _sql.Received(1).SubsumesAsync(parameters, Arg.Any<CancellationToken>());
        await _fallback.DidNotReceiveWithAnyArgs().SubsumesAsync(default!, default);
        await _importStatus.DidNotReceiveWithAnyArgs().GetImportStatusAsync(default!, default);
    }

    [Fact]
    public async Task GivenAnImportStatusProvider_WhenAskedForImportStatus_ThenItDelegatesToTheProvider()
    {
        // Arrange
        GivenImportStatus(ValueSetUrl, TerminologyImportStatus.Completed);

        // Act
        var status = await CreateHybrid()
            .GetImportStatusAsync(ValueSetUrl, CancellationToken.None);

        // Assert
        status.ShouldBe(TerminologyImportStatus.Completed);
        await _importStatus.Received(1).GetImportStatusAsync(ValueSetUrl, Arg.Any<CancellationToken>());
    }
}

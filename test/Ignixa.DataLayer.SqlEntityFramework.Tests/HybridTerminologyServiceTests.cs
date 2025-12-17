// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.DataLayer.SqlEntityFramework.Features.Terminology;
using Ignixa.Domain.Terminology;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests;

public class HybridTerminologyServiceTests
{
    [Fact]
    public async Task ValidateBinding_RoutesToSql_WhenImported()
    {
        var sql = Substitute.For<ITerminologyService>();
        var fallback = Substitute.For<ITerminologyService>();

        sql.GetImportStatusAsync("http://example.org/vs", Arg.Any<CancellationToken>())
            .Returns(TerminologyImportStatus.Completed);

        sql.ValidateBindingAsync(
                Arg.Any<string>(),
                Arg.Any<BindingStrength>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BindingValidationResult(true, BindingStrength.Required, IssueSeverity.Information, null, null));

        var hybrid = new HybridTerminologyService(sql, fallback, NullLogger<HybridTerminologyService>.Instance);

        var result = await hybrid.ValidateBindingAsync(
            "http://example.org/vs",
            BindingStrength.Required,
            "http://example.org",
            "ABC",
            "Display",
            null,
            CancellationToken.None);

        await sql.Received().ValidateBindingAsync(
            "http://example.org/vs",
            BindingStrength.Required,
            "http://example.org",
            "ABC",
            "Display",
            null,
            Arg.Any<CancellationToken>());
        await fallback.DidNotReceiveWithAnyArgs().ValidateBindingAsync(default!, default, default, default, default, default, default);
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateBinding_RoutesToFallback_WhenNotImported()
    {
        var sql = Substitute.For<ITerminologyService>();
        var fallback = Substitute.For<ITerminologyService>();

        sql.GetImportStatusAsync("http://example.org/vs", Arg.Any<CancellationToken>())
            .Returns((TerminologyImportStatus?)null);

        fallback.ValidateBindingAsync(
                Arg.Any<string>(),
                Arg.Any<BindingStrength>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BindingValidationResult(true, BindingStrength.Required, IssueSeverity.Information, null, null));

        var hybrid = new HybridTerminologyService(sql, fallback, NullLogger<HybridTerminologyService>.Instance);

        var result = await hybrid.ValidateBindingAsync(
            "http://example.org/vs",
            BindingStrength.Required,
            "http://example.org",
            "ABC",
            "Display",
            null,
            CancellationToken.None);

        await fallback.Received().ValidateBindingAsync(
            "http://example.org/vs",
            BindingStrength.Required,
            "http://example.org",
            "ABC",
            "Display",
            null,
            Arg.Any<CancellationToken>());
        await sql.DidNotReceive().ValidateBindingAsync(
            Arg.Any<string>(),
            Arg.Any<BindingStrength>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
        result.IsValid.ShouldBeTrue();
    }
}

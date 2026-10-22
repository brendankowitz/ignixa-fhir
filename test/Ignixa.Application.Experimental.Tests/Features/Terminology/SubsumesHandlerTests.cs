// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Experimental.Terminology.Subsumes;
using Ignixa.Domain.Exceptions;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Abstractions.Terminology;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Experimental.Tests.Features.Terminology;

public class SubsumesHandlerTests
{
    private const string System = "http://example.org/cs";

    private readonly ITerminologyService _terminologyService = Substitute.For<ITerminologyService>();

    private SubsumesHandler CreateHandler() => new(_terminologyService, NullLogger<SubsumesHandler>.Instance);

    [Fact]
    public async Task GivenUndeterminedOutcome_WhenHandled_ThenBadRequestNamesTheReason()
    {
        // Regression: an unknown code reached the client as a well-formed not-subsumed result.
        _terminologyService.SubsumesAsync(Arg.Any<SubsumesRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SubsumesOutcome.Undetermined(new UnknownReason(
                UnknownKind.CodeNotInSystem,
                $"{System}#b",
                "Code 'b' is not defined")));

        var exception = await Should.ThrowAsync<BadRequestException>(
            () => CreateHandler().HandleAsync(new SubsumesQuery(1, "a", "b", System, null), CancellationToken.None));

        exception.Message.ShouldContain("Code 'b' is not defined");
    }

    [Fact]
    public async Task GivenUnsupportedContent_WhenHandled_ThenUnprocessableExceptionNamesTheReason()
    {
        // Mapping this reason to BadRequestException loses the unsupported-content distinction required by
        // $subsumes and incorrectly makes a package hierarchy limitation look like invalid client input.
        _terminologyService.SubsumesAsync(Arg.Any<SubsumesRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SubsumesOutcome.Undetermined(new UnknownReason(
                UnknownKind.UnsupportedFilter,
                System,
                "Package terminology content carries no concept hierarchy")));

        var exception = await Should.ThrowAsync<TerminologyOperationNotSupportedException>(
            () => CreateHandler().HandleAsync(new SubsumesQuery(1, "a", "b", System, null), CancellationToken.None));

        exception.StatusCode.ShouldBe(422);
        exception.Message.ShouldBe("Cannot determine subsumption: Package terminology content carries no concept hierarchy");
    }

    [Theory]
    [InlineData(SubsumptionRelation.Subsumes, "subsumes")]
    [InlineData(SubsumptionRelation.NotSubsumed, "not-subsumed")]
    public async Task GivenKnownRelation_WhenHandled_ThenOutcomeCodeIsReturned(SubsumptionRelation relation, string code)
    {
        // Returning an internal enum name rather than the FHIR outcome code breaks clients.
        _terminologyService.SubsumesAsync(Arg.Any<SubsumesRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SubsumesOutcome.Related(relation));

        var result = await CreateHandler().HandleAsync(new SubsumesQuery(1, "a", "b", System, null), CancellationToken.None);

        result.ParametersResource["parameter"]![0]!["valueCode"]!.GetValue<string>().ShouldBe(code);
    }
}

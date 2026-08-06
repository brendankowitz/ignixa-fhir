// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Application.Features.Experimental.Terminology.Expand;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Experimental.Tests.Features.Terminology;

/// <summary>
/// The shape <see cref="ExpandValueSetHandler"/> hands back to the endpoint. These assert the JSON rather
/// than the <see cref="ExpandResult"/> it came from, because the defect they cover was entirely in the
/// projection: the handler built its response without reading <c>Incomplete</c>, so a partial expansion
/// reached the client claiming to be whole.
/// </summary>
public class ExpandValueSetHandlerTests
{
    private const string Url = "http://example.org/fhir/ValueSet/expand-handler";

    private readonly ITerminologyService _terminologyService = Substitute.For<ITerminologyService>();

    private ExpandValueSetHandler CreateHandler() =>
        new(_terminologyService, NullLogger<ExpandValueSetHandler>.Instance);

    private static ExpandResult Expansion(
        IReadOnlyList<ExpandedConcept> contains, bool incomplete = false) =>
        new(
            Identifier: "urn:uuid:6f1c1a3e-0000-4000-8000-000000000001",
            Timestamp: DateTimeOffset.UnixEpoch,
            Total: contains.Count,
            Offset: 0,
            Contains: contains,
            Incomplete: incomplete);

    private async Task<JsonNode> ExpandAsync(ExpandResult result)
    {
        _terminologyService
            .ExpandValueSetAsync(Arg.Any<ExpansionParameters>(), Arg.Any<CancellationToken>())
            .Returns(result);

        var response = await CreateHandler().HandleAsync(
            new ExpandValueSetQuery(TenantId: 1, Url: Url), CancellationToken.None);

        return response.ValueSetResource["expansion"]!;
    }

    [Fact]
    public async Task GivenAnEmptyExpansion_WhenHandled_ThenItReportsZeroCodesRatherThanFailing()
    {
        // Arrange
        var result = Expansion([]);

        // Act
        var expansion = await ExpandAsync(result);

        // Assert
        expansion["total"]!.GetValue<int>().ShouldBe(0);
        expansion["contains"]!.AsArray().Count.ShouldBe(0);
    }

    [Fact]
    public async Task GivenAPartialExpansion_WhenHandled_ThenTheResponseSaysItIsIncomplete()
    {
        // Arrange
        var result = Expansion([], incomplete: true);

        // Act
        var expansion = await ExpandAsync(result);

        // Assert
        expansion["incomplete"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task GivenACompleteExpansion_WhenHandled_ThenNoIncompleteFlagIsWritten()
    {
        // Arrange
        var result = Expansion(
            [new ExpandedConcept("http://example.org/fhir/CodeSystem/vehicles", "car", "Car")]);

        // Act
        var expansion = await ExpandAsync(result);

        // Assert
        expansion["incomplete"].ShouldBeNull();
        expansion["contains"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public async Task GivenAValueSetTheServiceCannotFind_WhenHandled_ThenItThrows()
    {
        // Arrange
        _terminologyService
            .ExpandValueSetAsync(Arg.Any<ExpansionParameters>(), Arg.Any<CancellationToken>())
            .Returns((ExpandResult)null);

        // Act & Assert
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => CreateHandler().HandleAsync(
                new ExpandValueSetQuery(TenantId: 1, Url: Url), CancellationToken.None));

        exception.Message.ShouldContain(Url);
    }
}

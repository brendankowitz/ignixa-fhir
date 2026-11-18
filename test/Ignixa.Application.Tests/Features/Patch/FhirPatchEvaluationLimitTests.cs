// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Patch;
using Ignixa.Application.Features.Patch.Executors;
using Ignixa.Application.Features.Search;
using Ignixa.Domain.Exceptions;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Serialization.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.Patch;

/// <summary>
/// FHIRPath Patch paths come from the request, so their evaluation is bounded and cancellable (#531): a path that
/// exceeds an evaluation limit is reported as too costly, and the request's token stops evaluation.
/// </summary>
public sealed class FhirPatchEvaluationLimitTests : IDisposable
{
    /// <summary>
    /// The lookahead forces the backtracking regex engine and <c>(a+)+</c> inside it is exponential on a run of
    /// <c>a</c> with no <c>b</c>, so the match times out (one second) - a resource limit the request chose.
    /// </summary>
    private const string TimingOutPath = "Patient.name.where(family.matches('(?=(a+)+b)'))";

    private readonly FhirVersionContext _versionContext = new(
        Substitute.For<ILoggerFactory>(),
        new Ignixa.Search.Definition.SearchParameterResolutionOptions(),
        NullFhirBaseUriProvider.Instance);

    public void Dispose() => _versionContext.Dispose();

    [Fact]
    public async Task GivenAPathThatExceedsAnEvaluationLimit_WhenPatching_ThenTheRequestIsTooCostly()
    {
        // Arrange - guard (new behaviour, not a regression): the executor wrapped the limit, an InvalidOperationException, as an invalid patch.
        var patient = PatientWithLongFamily();
        var operation = new FhirPatchOperation { Type = FhirPatchOperationType.Replace, Path = TimingOutPath, Value = "x" };

        // Act
        var exception = await Should.ThrowAsync<RequestTooCostlyException>(() => ApplyAsync(patient, operation, CancellationToken.None));

        // Assert
        exception.Message.ShouldContain("exceeded a FHIRPath evaluation limit");
    }

    [Fact]
    public async Task GivenACancelledRequest_WhenPatching_ThenEvaluationObservesTheToken()
    {
        // Arrange - guard (new behaviour, not a regression): the executors did not pass their token to the mutator, so a cancelled request still
        // evaluated every path.
        var patient = PatientWithLongFamily();
        var operation = new FhirPatchOperation { Type = FhirPatchOperationType.Replace, Path = "Patient.name.family", Value = "x" };

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => ApplyAsync(patient, operation, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task GivenAPathNestedBeyondTheParserLimit_WhenPatching_ThenThePatchIsRejectedAsInvalid()
    {
        // Arrange: the parser refuses the path with FormatException, which must reach the client as a bad patch,
        // not as a server error.
        var patient = PatientWithLongFamily();
        var nested = new string('(', 17) + "true" + new string(')', 17);
        var operation = new FhirPatchOperation { Type = FhirPatchOperationType.Replace, Path = $"Patient.name.where({nested}).family", Value = "x" };

        // Act
        var exception = await Should.ThrowAsync<FhirPatchException>(() => ApplyAsync(patient, operation, CancellationToken.None));

        // Assert
        exception.InnerException.ShouldBeOfType<FormatException>();
    }

    [Fact]
    public async Task GivenAPathContainingLineBreaks_WhenItsParentIsMissing_ThenTheErrorMessageHasNoLineBreaks()
    {
        // Arrange: a FHIRPath comment can carry line breaks, and the path is quoted in the exception message that is
        // logged.
        var patient = PatientWithLongFamily();
        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Add,
            Path = "Patient.contact /*\r\nsecond line*/.name",
            Value = "x",
        };

        // Act
        var exception = await Should.ThrowAsync<FhirPatchException>(() => ApplyAsync(patient, operation, CancellationToken.None));

        // Assert
        exception.Message.ShouldNotContain('\r');
        exception.Message.ShouldNotContain('\n');
    }

    private static ResourceJsonNode PatientWithLongFamily() =>
        JsonSourceNodeFactory.Parse<ResourceJsonNode>(
            $$"""{"resourceType":"Patient","id":"p","name":[{"family":"{{new string('a', 40)}}"}]}""");

    private async Task<ResourceJsonNode> ApplyAsync(ResourceJsonNode resource, FhirPatchOperation operation, CancellationToken cancellationToken)
    {
        var schema = _versionContext.GetBaseSchemaProvider(FhirVersion.R4);
        var mutator = new JsonNodeMutator(new Ignixa.FhirPath.Evaluation.FhirPathEvaluator(), new FhirPathParser(), () => schema);
        var engine = new FhirPatchEngine(
            NullLogger<FhirPatchEngine>.Instance,
            [
                new ReplaceOperationExecutor(NullLogger<ReplaceOperationExecutor>.Instance, mutator),
                new AddOperationExecutor(NullLogger<AddOperationExecutor>.Instance, mutator),
            ]);

        return await engine.ApplyPatchAsync(resource, [operation], schema, cancellationToken);
    }
}

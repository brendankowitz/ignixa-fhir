// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Infrastructure.Behaviors;
using Ignixa.Application.Tests.TestDoubles;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Serialization;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure.Behaviors;

/// <summary>
/// Validation warnings on an accepted write are logged for every create and update, and their messages can quote
/// the resource's values (a terminology warning names the code it could not verify), so only the issue code is logged.
/// </summary>
public class ValidationBehaviorLoggingTests
{
    private const string ResourceValue = "http://snomed.info/sct#44054006";

    [Fact]
    public async Task GivenAWarningQuotingAResourceValue_WhenTheWriteIsValidated_ThenTheValueDoesNotReachTheLog()
    {
        // Arrange
        var logger = new CapturingLogger<ValidationBehavior>();
        var warning = new ValidationIssue(
            IssueSeverity.Warning,
            "terminology-validation-warning",
            "Condition.code.coding[0]",
            $"Unable to verify code '{ResourceValue}' against value set 'http://example.org/vs'");
        var behavior = BuildBehavior(new FixedResultCheck(new ValidationResult(isValid: true, issues: [warning])), logger);

        // Act
        await behavior.HandleAsync(CreateRequest(), () => Task.FromResult(CommittedResult()), CancellationToken.None);

        // Assert
        logger.Messages.ShouldNotContain(message => message.Contains(ResourceValue, StringComparison.Ordinal));
        logger.Messages.ShouldContain(message => message.Contains("terminology-validation-warning", StringComparison.Ordinal));
    }

    private static CreateOrUpdateResourceCommand CreateRequest() => new(
        "Condition",
        "condition-1",
        JsonSourceNodeFactory.Parse("""{ "resourceType": "Condition", "id": "condition-1" }"""),
        HttpMethod.Put);

    private static UpdateResult CommittedResult() =>
        new(new ResourceKey("Condition", "condition-1"), ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow);

    private static ValidationBehavior BuildBehavior(IValidationCheck check, ILogger<ValidationBehavior> logger)
    {
        var schema = new ValidationSchema(
            "http://hl7.org/fhir/StructureDefinition/Condition",
            "Condition",
            universalChecks: [check],
            specChecks: [],
            profileChecks: []);

        var schemaResolver = Substitute.For<IValidationSchemaResolver>();
        schemaResolver.GetSchema(Arg.Any<string>()).Returns(schema);

        var fhirContext = Substitute.For<IFhirRequestContext>();
        fhirContext.TenantId.Returns(1);
        fhirContext.TenantConfiguration.Returns(new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "R4",
            ValidationDepth = "Spec",
        });
        fhirContext.FhirVersion.Returns(FhirVersion.R4);

        var contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        contextAccessor.RequestContext.Returns(fhirContext);

        var versionContext = new FhirVersionContext(
            Substitute.For<ILoggerFactory>(),
            new SearchParameterResolutionOptions(),
            NullFhirBaseUriProvider.Instance);

        return new ValidationBehavior(
            contextAccessor,
            versionContext,
            (_, _) => schemaResolver,
            Substitute.For<ITerminologyService>(),
            logger);
    }

    private sealed class FixedResultCheck(ValidationResult result) : IValidationCheck
    {
        public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state) => result;
    }
}

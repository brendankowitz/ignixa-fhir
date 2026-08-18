// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------


using Ignixa.Abstractions;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Infrastructure.Behaviors;
using Ignixa.Domain.Models;
using Ignixa.FhirPath.Parser;
using Ignixa.Search.Definition;
using Ignixa.Serialization;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure.Behaviors;

/// <summary>
/// Pins that the create/update path hands validation a seeded resource scope, so invariants that
/// reference the containing resource actually evaluate.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ValidationBehavior"/> had no test at all, which is how it went unnoticed that it validated
/// every write with a bare <see cref="ValidationState"/>. With no scope, <c>FhirPathInvariantCheck</c> takes
/// its unseeded branch: <c>%resource</c> and <c>%rootResource</c> are empty and <c>resolve()</c> has no
/// resolver, so every root-referencing invariant evaluates to empty - which the check reads as false and
/// reports as a constraint failure. The resource was rejected for a defect in the harness, not in the data.
/// </para>
/// <para>
/// The engine cannot paper over this: <see cref="IElement"/> carries no parent link, so nothing downstream
/// can find the containing resource. The caller is the only party that knows it, which is exactly why this
/// has to be asserted here rather than in the FHIRPath layer.
/// </para>
/// </remarks>
public class ValidationBehaviorResourceScopeTests
{
    private const string PatientJson = """
        {
            "resourceType": "Patient",
            "id": "patient-123",
            "active": true
        }
        """;

    [Fact]
    public async Task GivenACreateOrUpdate_WhenValidating_ThenChecksReceiveTheResourceScope()
    {
        // Arrange
        var recordingCheck = new ScopeRecordingCheck();
        var behavior = BuildBehavior(recordingCheck, out var request);

        // Act
        await behavior.HandleAsync(request, () => Task.FromResult(new ResourceKey("Patient", "patient-123")), CancellationToken.None);

        // Assert
        recordingCheck.WasInvoked.ShouldBeTrue();
        recordingCheck.ObservedResource.ShouldNotBeNull();
        recordingCheck.ObservedResource!.InstanceType.ShouldBe("Patient");
        recordingCheck.ObservedRootResource.ShouldNotBeNull();
        recordingCheck.ObservedResolver.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenAnInvariantReferencingResource_WhenValidatingOnTheWritePath_ThenItEvaluatesAgainstTheResource()
    {
        // Arrange - a constraint that is true of this Patient, and can only be decided if %resource resolves.
        // With an unseeded scope %resource is empty, the comparison yields empty, and the check reads that as
        // a failed constraint - so this resource would be rejected for a reason that has nothing to do with it.
        var schemaProvider = BuildVersionContext().GetBaseSchemaProvider(FhirVersion.R4);
        var invariant = new FhirPathInvariantCheck(
            new ConstraintDefinition
            {
                Key = "test-resource-scope-1",
                Expression = "%resource.id = 'patient-123'",
                Human = "The write path must supply %resource to invariants",
                Severity = "error",
            },
            schemaProvider,
            new FhirPathParser());

        // Wraps the real invariant so the test can tell "validation passed" apart from "validation never
        // ran": with only result.ShouldNotBeNull()/Id assertions, a behavior that silently skipped the
        // Profile/Full tier entirely (e.g. a broken depth or tier filter) would return the same successful
        // ResourceKey and this test would pass for the wrong reason.
        var recordingInvariant = new InvocationRecordingCheck(invariant);

        // Profile tier at Full depth: that is where FhirPathInvariantCheck actually runs, so this is the
        // configuration in which the missing scope bites. At the default Spec depth invariants are skipped
        // entirely, which is why the defect stayed invisible.
        var behavior = BuildBehavior(recordingInvariant, out var request, ValidationTier.Profile, ValidationDepth.Full);

        // Act
        var result = await behavior.HandleAsync(
            request,
            () => Task.FromResult(new ResourceKey("Patient", "patient-123")),
            CancellationToken.None);

        // Assert
        recordingInvariant.WasInvoked.ShouldBeTrue("the Profile/Full validation path must actually run the invariant, not just skip through to success");
        result.ShouldNotBeNull();
        result.Id.ShouldBe("patient-123");
    }

    private enum ValidationTier
    {
        Universal,
        Profile,
    }

    private static ValidationBehavior BuildBehavior(
        IValidationCheck check,
        out CreateOrUpdateResourceCommand request,
        ValidationTier tier = ValidationTier.Universal,
        ValidationDepth? depthOverride = null)
    {
        var versionContext = BuildVersionContext();

        var schema = new ValidationSchema(
            "http://hl7.org/fhir/StructureDefinition/Patient",
            "Patient",
            universalChecks: tier == ValidationTier.Universal ? [check] : [],
            specChecks: [],
            profileChecks: tier == ValidationTier.Profile ? [check] : []);

        var schemaResolver = Substitute.For<IValidationSchemaResolver>();
        schemaResolver.GetSchema(Arg.Any<string>()).Returns(schema);

        var tenantConfiguration = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "R4",
            ValidationDepth = "Spec",
        };

        var fhirContext = Substitute.For<IFhirRequestContext>();
        fhirContext.TenantConfiguration.Returns(tenantConfiguration);
        fhirContext.FhirVersion.Returns(FhirVersion.R4);

        var contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        contextAccessor.RequestContext.Returns(fhirContext);

        request = new CreateOrUpdateResourceCommand(
            "Patient",
            "patient-123",
            JsonSourceNodeFactory.Parse(PatientJson),
            HttpMethod.Put,
            ValidationDepthOverride: depthOverride);

        return new ValidationBehavior(
            contextAccessor,
            versionContext,
            _ => schemaResolver,
            Substitute.For<ITerminologyService>(),
            NullLogger<ValidationBehavior>.Instance);
    }

    private static FhirVersionContext BuildVersionContext() => new(
        Substitute.For<ILoggerFactory>(),
        new SearchParameterResolutionOptions(),
        NullFhirBaseUriProvider.Instance);

    /// <summary>
    /// Captures the resource scope the behavior handed down, so the assertion is about the contract rather
    /// than about any particular shipped invariant that happens to reference <c>%resource</c>.
    /// </summary>
    private sealed class ScopeRecordingCheck : IValidationCheck
    {
        public bool WasInvoked { get; private set; }

        public IElement? ObservedResource { get; private set; }

        public IElement? ObservedRootResource { get; private set; }

        public Func<string, IElement?>? ObservedResolver { get; private set; }

        public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
        {
            WasInvoked = true;
            ObservedResource = state.Scope.Resource;
            ObservedRootResource = state.Scope.RootResource;
            ObservedResolver = state.Scope.Resolver;
            return ValidationResult.Success();
        }
    }

    /// <summary>
    /// Wraps a real <see cref="IValidationCheck"/> and records whether it was ever invoked, so a test can
    /// distinguish "the write succeeded because validation ran and passed" from "the write succeeded
    /// because validation was silently skipped".
    /// </summary>
    private sealed class InvocationRecordingCheck(IValidationCheck inner) : IValidationCheck
    {
        public bool WasInvoked { get; private set; }

        public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
        {
            WasInvoked = true;
            return inner.Validate(element, settings, state);
        }
    }
}

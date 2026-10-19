// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Claims;
using Ignixa.Api.Endpoints;
using Ignixa.Application.Features.Authorization;
using Ignixa.Application.Features.Authorization.Handlers;
using Ignixa.Application.Features.Authorization.Models;
using Ignixa.Application.Features.Authorization.Services;
using Ignixa.Application.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Endpoints;

/// <summary>
/// Drives <see cref="BulkDeleteKickoffAuthorizer"/> through the real authorization pipeline
/// (authentication, tenant isolation, local RBAC, SMART scopes) with real claims, so each case checks the
/// scope/role semantics a deployment would apply rather than a mocked decision.
/// </summary>
public sealed class BulkDeleteKickoffAuthorizerTests
{
    private const string PatientDeleterRole = "PatientDeleter";
    private const string PatientDeleteUpdateRole = "PatientDeleteUpdate";

    [Theory]
    [InlineData("patient/Patient.r")]
    [InlineData("system/Patient.rs")]
    [InlineData("system/*.rs")]
    [InlineData("system/Observation.cruds")]
    public async Task GivenAScopeWithoutDeleteOnTheType_WhenKickingOffATypeLevelBulkDelete_ThenItIsDenied(string scope)
    {
        var result = await AuthorizeAsync(SmartUser(scope, patient: "p1"), "Patient", []);

        result.Allowed.ShouldBeFalse();
        result.DenialReason.ShouldNotBeNull().ShouldContain("delete access to Patient");
    }

    [Theory]
    [InlineData("system/Patient.d")]
    [InlineData("system/Patient.cud")]
    [InlineData("system/*.d")]
    [InlineData("user/Patient.d")]
    public async Task GivenAnUnconstrainedDeleteScopeOnTheType_WhenKickingOffATypeLevelBulkDelete_ThenItIsAllowed(string scope)
    {
        var result = await AuthorizeAsync(SmartUser(scope), "Patient", []);

        result.Allowed.ShouldBeTrue(result.DenialReason);
    }

    [Fact]
    public async Task GivenOnlyPatientDelete_WhenKickingOffASystemLevelBulkDeleteWithoutType_ThenItIsDenied()
    {
        var result = await AuthorizeAsync(SmartUser("system/Patient.d"), null, []);

        result.Allowed.ShouldBeFalse();
        result.DenialReason.ShouldNotBeNull().ShouldContain("all resource types (*)");
    }

    [Fact]
    public async Task GivenDeleteOnEveryTypeNamedByType_WhenKickingOffASystemLevelBulkDelete_ThenItIsAllowed()
    {
        var result = await AuthorizeAsync(
            SmartUser("system/Patient.d system/DiagnosticReport.d"), null, [new("_type", "DiagnosticReport,Patient")]);

        result.Allowed.ShouldBeTrue(result.DenialReason);
    }

    [Fact]
    public async Task GivenDeleteOnOnlySomeTypesNamedByType_WhenKickingOffASystemLevelBulkDelete_ThenItIsDenied()
    {
        var result = await AuthorizeAsync(
            SmartUser("system/Patient.d"), null, [new("_type", "Patient"), new("_type", "DiagnosticReport")]);

        result.Allowed.ShouldBeFalse();
        result.DenialReason.ShouldNotBeNull().ShouldContain("delete access to DiagnosticReport");
    }

    [Theory]
    [InlineData("_revinclude", "Observation:subject")]
    [InlineData("_include", "Patient:organization")]
    [InlineData("_include:iterate", "Patient:organization")]
    public async Task GivenOnlyTypeDelete_WhenTheKickoffCascadesThroughIncludes_ThenItIsDenied(string name, string value)
    {
        var result = await AuthorizeAsync(SmartUser("system/Patient.d"), "Patient", [new(name, value)]);

        result.Allowed.ShouldBeFalse();
        result.DenialReason.ShouldNotBeNull().ShouldContain("_include/_revinclude");
    }

    [Fact]
    public async Task GivenWildcardDelete_WhenTheKickoffCascadesThroughRevInclude_ThenItIsAllowed()
    {
        var result = await AuthorizeAsync(
            SmartUser("system/*.d"), "Patient", [new("_revinclude", "Observation:subject")]);

        result.Allowed.ShouldBeTrue(result.DenialReason);
    }

    [Theory]
    [InlineData("patient/Patient.d")]
    [InlineData("patient/*.cruds")]
    public async Task GivenAPatientContextToken_WhenKickingOffABulkDelete_ThenItIsDeniedEvenWithDeleteScope(string scope)
    {
        var result = await AuthorizeAsync(SmartUser(scope, patient: "p1"), "Patient", []);

        result.Allowed.ShouldBeFalse();
        result.DenialReason.ShouldNotBeNull().ShouldContain("compartment or search constraint");
    }

    [Fact]
    public async Task GivenASearchConstrainedDeleteScope_WhenKickingOffABulkDelete_ThenItIsDenied()
    {
        var result = await AuthorizeAsync(SmartUser("system/Patient.d?identifier=abc"), "Patient", []);

        result.Allowed.ShouldBeFalse();
        result.DenialReason.ShouldNotBeNull().ShouldContain("compartment or search constraint");
    }

    [Fact]
    public async Task GivenRemoveReferencesWithDeleteButNoUpdate_WhenKickingOffABulkDelete_ThenItIsDenied()
    {
        var result = await AuthorizeAsync(
            SmartUser("system/*.d"), "Patient", [new("_hardDelete", "true"), new("_remove-references", "true")]);

        result.Allowed.ShouldBeFalse();
        result.DenialReason.ShouldNotBeNull().ShouldContain("update access to all resource types (*)");
    }

    [Fact]
    public async Task GivenRemoveReferencesWithUpdateOnEveryType_WhenKickingOffABulkDelete_ThenItIsAllowed()
    {
        var result = await AuthorizeAsync(
            SmartUser("system/Patient.d system/*.u"), "Patient", [new("_hardDelete", "true"), new("removeReferences", "true")]);

        result.Allowed.ShouldBeTrue(result.DenialReason);
    }

    [Fact]
    public async Task GivenARoleWithDeleteOnPatientOnly_WhenKickingOffATypeLevelBulkDelete_ThenItIsAllowed()
    {
        var result = await AuthorizeAsync(RoleUser(PatientDeleterRole), "Patient", []);

        result.Allowed.ShouldBeTrue(result.DenialReason);
    }

    [Fact]
    public async Task GivenARoleWithDeleteOnPatientOnly_WhenTheKickoffCascadesThroughRevInclude_ThenItIsDenied()
    {
        var result = await AuthorizeAsync(
            RoleUser(PatientDeleterRole), "Patient", [new("_revinclude", "Observation:subject")]);

        result.Allowed.ShouldBeFalse();
        result.DenialReason.ShouldNotBeNull().ShouldContain("delete access to all resource types (*)");
    }

    [Fact]
    public async Task GivenARoleWithPatientDeleteAndUpdate_WhenRemovingReferences_ThenUpdateOnEveryTypeIsStillRequired()
    {
        var result = await AuthorizeAsync(
            RoleUser(PatientDeleteUpdateRole), "Patient", [new("_hardDelete", "true"), new("_remove-references", "true")]);

        result.Allowed.ShouldBeFalse();
        result.DenialReason.ShouldNotBeNull().ShouldContain("update access to all resource types (*)");
    }

    [Theory]
    [InlineData("ReadOnly")]
    [InlineData("Clinician")]
    public async Task GivenARoleWithoutWildcardDelete_WhenKickingOffASystemLevelBulkDelete_ThenItIsDenied(string role)
    {
        var result = await AuthorizeAsync(RoleUser(role), null, []);

        result.Allowed.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenTheAdminRole_WhenKickingOffASystemLevelBulkDeleteWithRemoveReferences_ThenItIsAllowed()
    {
        var result = await AuthorizeAsync(
            RoleUser("Admin"), null, [new("_hardDelete", "true"), new("_remove-references", "true")]);

        result.Allowed.ShouldBeTrue(result.DenialReason);
    }

    [Fact]
    public async Task GivenAnAnonymousCaller_WhenKickingOffABulkDelete_ThenItIsDenied()
    {
        var result = await AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), "Patient", []);

        result.Allowed.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenAuthorizationIsDisabled_WhenKickingOffABulkDelete_ThenItIsAllowedWithoutConsultingTheAuthorizationService()
    {
        var service = Substitute.For<IFhirAuthorizationService>();
        var authorizer = new BulkDeleteKickoffAuthorizer(
            service,
            RequestContextAccessor(),
            Options.Create(new AuthorizationOptions { Enabled = false }),
            NullLogger<BulkDeleteKickoffAuthorizer>.Instance);

        var result = await authorizer.AuthorizeAsync(
            HttpContextFor(new ClaimsPrincipal(new ClaimsIdentity())), null, Parse([]), CancellationToken.None);

        result.Allowed.ShouldBeTrue();
        await service.DidNotReceiveWithAnyArgs().AuthorizeAsync(default!, default);
    }

    [Fact]
    public void GivenSystemLevelTypeParameters_WhenComputingRequirements_ThenDeleteIsRequiredOnEachDistinctTypeInOrdinalOrder()
    {
        var requirements = BulkDeleteKickoffAuthorizer.GetRequirements(
            null, Parse([new("_type", "Patient, Observation"), new("_type", "Condition,Patient")]));

        requirements.Select(requirement => (requirement.Interaction, requirement.ResourceType)).ShouldBe(
        [
            (FhirInteraction.Delete, "Condition"),
            (FhirInteraction.Delete, "Observation"),
            (FhirInteraction.Delete, "Patient"),
        ]);
    }

    [Fact]
    public void GivenTypeWithAModifier_WhenComputingRequirements_ThenItDoesNotNarrowTheTypeSetAndEveryTypeIsRequired()
    {
        // _type:not is a search filter, not a type selector: the job still targets every type.
        var requirement = BulkDeleteKickoffAuthorizer.GetRequirements(null, Parse([new("_type:not", "Patient")]))
            .ShouldHaveSingleItem();

        requirement.Interaction.ShouldBe(FhirInteraction.Delete);
        requirement.ResourceType.ShouldBeNull();
    }

    private static async Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        string? routeResourceType,
        KeyValuePair<string, string>[] query)
    {
        var accessor = RequestContextAccessor();
        var options = Options.Create(new AuthorizationOptions { Enabled = true });
        var store = new InMemoryRolePermissionStore(options);
        store.SetRolePermissions(PatientDeleterRole, [new ResourceGrant("Patient", "delete")]);
        store.SetRolePermissions(
            PatientDeleteUpdateRole, [new ResourceGrant("Patient", "delete"), new ResourceGrant("Patient", "update")]);
        IAuthorizationHandler[] handlers =
        [
            new AuthenticationHandler(NullLogger<AuthenticationHandler>.Instance),
            new TenantIsolationHandler(accessor, NullLogger<TenantIsolationHandler>.Instance),
            new RbacAuthorizationHandler(store, NullLogger<RbacAuthorizationHandler>.Instance),
            new SmartScopeAuthorizationHandler(NullLogger<SmartScopeAuthorizationHandler>.Instance),
        ];
        var authorizer = new BulkDeleteKickoffAuthorizer(
            new FhirAuthorizationService(handlers, NullLogger<FhirAuthorizationService>.Instance),
            accessor,
            options,
            NullLogger<BulkDeleteKickoffAuthorizer>.Instance);

        return await authorizer.AuthorizeAsync(HttpContextFor(user), routeResourceType, Parse(query), CancellationToken.None);
    }

    private static BulkDeleteRequestParseResult Parse(KeyValuePair<string, string>[] query) =>
        BulkDeleteRequestParser.Parse(query, "respond-async", ReadOnlyMemory<byte>.Empty);

    private static IFhirRequestContextAccessor RequestContextAccessor()
    {
        var requestContext = Substitute.For<IFhirRequestContext>();
        requestContext.TenantId.Returns(1);
        var accessor = Substitute.For<IFhirRequestContextAccessor>();
        accessor.RequestContext.Returns(requestContext);
        return accessor;
    }

    private static HttpContext HttpContextFor(ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        httpContext.Request.Method = HttpMethods.Delete;
        httpContext.Request.Path = "/tenant/1/$bulk-delete";
        return httpContext;
    }

    private static ClaimsPrincipal SmartUser(string scopes, string? patient = null)
    {
        List<Claim> claims = [new(FhirClaimTypes.Subject, "client-1"), new(FhirClaimTypes.Scope, scopes)];
        if (patient is not null)
        {
            claims.Add(new Claim(FhirClaimTypes.Patient, patient));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static ClaimsPrincipal RoleUser(string role) =>
        new(new ClaimsIdentity([new Claim(FhirClaimTypes.Subject, "user-1"), new Claim(FhirClaimTypes.Role, role)], "Test"));
}

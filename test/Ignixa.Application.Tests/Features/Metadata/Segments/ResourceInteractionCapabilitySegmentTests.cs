// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Application.Features.Search;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.Metadata.Segments;

/// <summary>
/// An interaction the server serves but does not declare is indistinguishable from one it does not
/// implement: conformance clients gate on the CapabilityStatement and silently skip the tests,
/// reporting neither pass nor fail. These assert the declared set matches the routes that exist.
/// </summary>
public class ResourceInteractionCapabilitySegmentTests
{
    private readonly ResourceInteractionCapabilitySegment _segment;

    public ResourceInteractionCapabilitySegmentTests()
    {
        var schemaProvider = Substitute.For<IFhirSchemaProvider>();
        schemaProvider.ResourceTypeNames.Returns(new HashSet<string> { "Patient", "AuditEvent" });

        var versionContext = Substitute.For<IFhirVersionContext>();
        versionContext.GetSchemaProvider(Arg.Any<FhirVersion>(), Arg.Any<int?>()).Returns(schemaProvider);

        _segment = new ResourceInteractionCapabilitySegment(
            versionContext,
            NullLogger<ResourceInteractionCapabilitySegment>.Instance);
    }

    [Theory]
    [InlineData(TypeRestfulInteraction.HistoryInstance)]
    [InlineData(TypeRestfulInteraction.HistoryType)]
    [InlineData(TypeRestfulInteraction.Patch)]
    public async Task GivenAnImplementedResourceInteraction_WhenApplyingSegment_ThenItIsDeclared(
        TypeRestfulInteraction interaction)
    {
        var patient = await ApplyAndGetResourceAsync("Patient");

        patient.Interaction.ShouldNotBeNull();
        patient.Interaction!.Select(i => i.Code).ShouldContain(interaction);
    }

    [Fact]
    public async Task GivenSystemLevelHistoryIsServed_WhenApplyingSegment_ThenHistorySystemIsDeclared()
    {
        var rest = await ApplyAndGetRestAsync();

        rest.Interaction.ShouldNotBeNull();
        rest.Interaction!.Select(i => i.Code).ShouldContain(SystemRestfulInteraction.HistorySystem);
    }

    [Fact]
    public async Task GivenNoVreadEndpointExists_WhenApplyingSegment_ThenVreadIsNotDeclared()
    {
        var patient = await ApplyAndGetResourceAsync("Patient");

        patient.Interaction!.Select(i => i.Code).ShouldNotContain(
            TypeRestfulInteraction.Vread,
            "No route serves vread; declaring it would make conformance clients run tests the server cannot satisfy.");
    }

    [Fact]
    public async Task GivenAuditEvent_WhenApplyingSegment_ThenMutatingInteractionsAreExcludedButHistoryIsNot()
    {
        var auditEvent = await ApplyAndGetResourceAsync("AuditEvent");
        var codes = auditEvent.Interaction!.Select(i => i.Code).ToList();

        codes.ShouldNotContain(TypeRestfulInteraction.Update);
        codes.ShouldNotContain(TypeRestfulInteraction.Delete);
        codes.ShouldNotContain(TypeRestfulInteraction.Patch);
        codes.ShouldContain(TypeRestfulInteraction.HistoryInstance);
    }

    private async Task<RestComponentJsonNode> ApplyAndGetRestAsync()
    {
        var statement = new CapabilityStatementJsonNode();
        var context = new CapabilityContext(FhirVersion: FhirVersion.R4, TenantId: 1);

        await _segment.ApplyAsync(statement, context, CancellationToken.None);

        statement.Rest.ShouldNotBeNull();
        statement.Rest!.ShouldNotBeEmpty();
        return statement.Rest[0];
    }

    private async Task<ResourceComponentJsonNode> ApplyAndGetResourceAsync(string resourceType)
    {
        var rest = await ApplyAndGetRestAsync();

        rest.Resource.ShouldNotBeNull();
        var resource = rest.Resource!.SingleOrDefault(r => r.Type == resourceType);
        resource.ShouldNotBeNull($"Expected a {resourceType} resource component in the CapabilityStatement.");
        return resource!;
    }
}

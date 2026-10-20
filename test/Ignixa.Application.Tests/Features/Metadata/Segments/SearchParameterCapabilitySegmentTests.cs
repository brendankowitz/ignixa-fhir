// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Application.Features.Search;
using Ignixa.Search.Definition;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.Metadata.Segments;

public class SearchParameterCapabilitySegmentTests
{
    [Fact]
    public async Task GivenDerivedReferenceIdentifierParameters_WhenApplyingSegment_ThenTheyAreNotDeclared()
    {
        var manager = new SearchParameterDefinitionManager(
            new R4CoreSchemaProvider(),
            NullLogger<SearchParameterDefinitionManager>.Instance);
        var versionContext = Substitute.For<IFhirVersionContext>();
        versionContext
            .GetSearchParameterDefinitionManager(FhirVersion.R4, 1)
            .Returns(manager);
        var segment = new SearchParameterCapabilitySegment(
            versionContext,
            NullLogger<SearchParameterCapabilitySegment>.Instance);
        var statement = new CapabilityStatementJsonNode();
        var rest = new RestComponentJsonNode();
        var encounter = new ResourceComponentJsonNode { Type = "Encounter" };
        rest.Resource.Add(encounter);
        statement.Rest.Add(rest);

        await segment.ApplyAsync(
            statement,
            new CapabilityContext(FhirVersion.R4, TenantId: 1),
            CancellationToken.None);

        encounter.SearchParam.Select(parameter => parameter.Name).ShouldContain("subject");
        encounter.SearchParam.Select(parameter => parameter.Name).ShouldNotContain("subject:identifier");
    }
}

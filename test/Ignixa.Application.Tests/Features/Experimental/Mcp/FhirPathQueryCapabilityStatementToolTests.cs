// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.Mcp.Tools.FhirOperations;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Medino;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.Mcp;

/// <summary>
/// The FHIRPath expression is the MCP caller's, so its evaluation observes the request's token (#531).
/// </summary>
public class FhirPathQueryCapabilityStatementToolTests
{
    [Fact]
    public async Task GivenACancelledRequest_WhenQuerying_ThenTheCancellationPropagatesInsteadOfBecomingAResultError()
    {
        // Arrange - regression: a catch-all turned every evaluation failure, cancellation included, into an entry in
        // the result's error list, so a cancelled request still evaluated and "succeeded".
        var tool = CreateTool();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(() =>
            tool.FhirPathQueryCapabilityStatementAsync("rest.resource.type", tenantId: 1, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task GivenAnInvalidExpression_WhenQuerying_ThenTheFailureIsReportedInTheResult()
    {
        // Arrange - the control: an ordinary evaluation failure is still reported to the caller, not thrown.
        var tool = CreateTool();

        // Act
        var result = await tool.FhirPathQueryCapabilityStatementAsync("(1 | 2 | 3) & 'b'", tenantId: 1, CancellationToken.None);

        // Assert
        result.Errors.ShouldHaveSingleItem();
    }

    private static FhirPathQueryCapabilityStatementTool CreateTool()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<GetCapabilityStatementQuery>(), Arg.Any<CancellationToken>())
            .Returns(JsonSourceNodeFactory.Parse<CapabilityStatementJsonNode>(
                """{"resourceType":"CapabilityStatement","status":"active","kind":"instance","fhirVersion":"4.0.1","rest":[{"mode":"server","resource":[{"type":"Patient"}]}]}"""));
        var accessor = Substitute.For<IFhirRequestContextAccessor>();
        accessor.RequestContext.Returns(new FhirRequestContext { TenantId = 1 });
        var store = Substitute.For<ITenantConfigurationStore>();
        store.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new TenantConfiguration { TenantId = 1, DisplayName = "MCP test", FhirVersion = "4.0" });
        var versionContext = Substitute.For<IFhirVersionContext>();
        versionContext.GetBaseSchemaProvider(FhirVersion.R4).Returns(FhirVersion.R4.GetSchemaProvider());

        return new FhirPathQueryCapabilityStatementTool(accessor, store, mediator, new FhirPathEvaluator(), new FhirPathParser(), versionContext);
    }
}

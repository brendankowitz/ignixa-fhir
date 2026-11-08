// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Grpc.Core;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Sidecar.Audit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Infrastructure.Audit;

public class SidecarAuditLoggerTests
{
    private readonly AuditService.AuditServiceClient _client = Substitute.For<AuditService.AuditServiceClient>();
    private readonly List<AuditEventRequest> _requests = [];
    private readonly SidecarAuditLogger _logger;

    public SidecarAuditLoggerTests()
    {
        _client.LogAuditEventAsync(
                Arg.Do<AuditEventRequest>(_requests.Add),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<AuditEventResponse>(
                Task.FromResult(new AuditEventResponse { Success = true }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
        _logger = new SidecarAuditLogger(_client, NullLogger<SidecarAuditLogger>.Instance);
    }

    [Fact]
    public void GivenHttpEventWithCustomHeaders_WhenLogged_ThenContractCustomHeadersCarryThemVerbatim()
    {
        _logger.LogHttpRequest(new HttpRequestAuditEvent
        {
            Action = "C",
            Outcome = "0",
            UserId = "user-1",
            ClientIp = "10.0.0.1",
            Method = "POST",
            Path = "/tenant/1",
            StatusCode = 200,
            DurationMs = 5,
            CorrelationId = "corr-1",
            CustomHeaders = new Dictionary<string, string>
            {
                ["X-IGNIXA-AUDIT-OPERATIONID"] = "op-1",
                ["X-IGNIXA-AUDIT-BUNDLEID"] = "bundle-1"
            }
        });

        var request = _requests.ShouldHaveSingleItem();
        request.CustomHeaders.Count.ShouldBe(2);
        request.CustomHeaders["X-IGNIXA-AUDIT-OPERATIONID"].ShouldBe("op-1");
        request.CustomHeaders["X-IGNIXA-AUDIT-BUNDLEID"].ShouldBe("bundle-1");
        request.CustomProperties.Keys.ShouldNotContain("X-IGNIXA-AUDIT-OPERATIONID");
    }

    [Fact]
    public void GivenBackgroundJobEvent_WhenLogged_ThenContractIdentifiesJobAndCarriesKickoffHeaders()
    {
        _logger.LogBackgroundJobCompleted(new BackgroundJobAuditEvent
        {
            JobType = "Export",
            JobId = "job-1",
            TenantId = 3,
            Status = "Failed",
            Outcome = "8",
            UserId = "user-1",
            CorrelationId = "corr-1",
            DurationMs = 1500,
            CustomHeaders = new Dictionary<string, string> { ["X-IGNIXA-AUDIT-OPERATIONID"] = "op-1" }
        });

        var request = _requests.ShouldHaveSingleItem();
        request.TenantId.ShouldBe("3");
        request.UserId.ShouldBe("user-1");
        request.CorrelationId.ShouldBe("corr-1");
        request.Success.ShouldBeFalse();
        request.CustomProperties["event"].ShouldBe("background-job-completed");
        request.CustomProperties["jobType"].ShouldBe("Export");
        request.CustomProperties["jobId"].ShouldBe("job-1");
        request.CustomProperties["status"].ShouldBe("Failed");
        request.CustomProperties["durationMs"].ShouldBe("1500");
        request.CustomHeaders["X-IGNIXA-AUDIT-OPERATIONID"].ShouldBe("op-1");
    }
}

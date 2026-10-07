// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Ignixa.Application.Tests.Infrastructure.Audit;

public class AuditLoggerTests
{
    private readonly RecordingLogger _logger = new();

    [Fact]
    public void GivenHttpEventWithCustomHeaders_WhenLogged_ThenStructuredCustomHeadersPropertyIsWritten()
    {
        new AuditLogger(_logger).LogHttpRequest(HttpEvent("0", new Dictionary<string, string>
        {
            ["X-IGNIXA-AUDIT-OPERATIONID"] = "op-1",
            ["X-IGNIXA-AUDIT-BUNDLEID"] = "bundle-1"
        }));

        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Properties["CustomHeaders"]
            .ShouldBe("X-IGNIXA-AUDIT-BUNDLEID=bundle-1;X-IGNIXA-AUDIT-OPERATIONID=op-1");
        entry.Message.ShouldContain("CustomHeaders=X-IGNIXA-AUDIT-BUNDLEID=bundle-1;");
    }

    [Fact]
    public void GivenFailedHttpEventWithInjectedLineBreak_WhenLogged_ThenWarningHasNoLineBreak()
    {
        new AuditLogger(_logger).LogHttpRequest(HttpEvent("4", new Dictionary<string, string>
        {
            ["X-IGNIXA-AUDIT-SITE"] = "a\r\nAUDIT: forged"
        }));

        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldNotContain("\n");
        entry.Properties["CustomHeaders"].ShouldBe("X-IGNIXA-AUDIT-SITE=a  AUDIT: forged");
    }

    [Fact]
    public void GivenBackgroundJobEvent_WhenLogged_ThenJobAttributionAndCustomHeadersAreWritten()
    {
        new AuditLogger(_logger).LogBackgroundJobCompleted(new BackgroundJobAuditEvent
        {
            JobType = "Export",
            JobId = "job-1",
            TenantId = 1,
            Status = "Completed",
            Outcome = "0",
            UserId = "user-1",
            CorrelationId = "corr-1",
            CustomHeaders = new Dictionary<string, string> { ["X-IGNIXA-AUDIT-OPERATIONID"] = "op-1" }
        });

        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Properties["JobType"].ShouldBe("Export");
        entry.Properties["JobId"].ShouldBe("job-1");
        entry.Properties["UserId"].ShouldBe("user-1");
        entry.Properties["CorrelationId"].ShouldBe("corr-1");
        entry.Properties["CustomHeaders"].ShouldBe("X-IGNIXA-AUDIT-OPERATIONID=op-1");
    }

    private static HttpRequestAuditEvent HttpEvent(string outcome, IReadOnlyDictionary<string, string> headers) => new()
    {
        Action = "R",
        Outcome = outcome,
        UserId = "user-1",
        ClientIp = "127.0.0.1",
        Method = "GET",
        Path = "/tenant/1/Patient/1",
        StatusCode = outcome == "0" ? 200 : 404,
        DurationMs = 1,
        CustomHeaders = headers
    };

    private sealed record LogEntry(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Properties);

    private sealed class RecordingLogger : ILogger<AuditLogger>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IReadOnlyList<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), properties));
        }
    }
}

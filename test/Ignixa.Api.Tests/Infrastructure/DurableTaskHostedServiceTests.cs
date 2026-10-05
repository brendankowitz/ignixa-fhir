using System.Collections.Concurrent;
using DurableTask.Core;
using Ignixa.Api.Infrastructure;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Ignixa.Api.Tests.Infrastructure;

public class DurableTaskHostedServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenCompletedShutdown_WhenConcurrentStopsFollowDependencyDisposal_ThenNoDisposedDependencyIsAccessed(
        bool disposeLogging)
    {
        var orchestrationService = Substitute.For<IOrchestrationService>();
        using var worker = new TaskHubWorker(orchestrationService);
        var logger = new ShutdownLogger();
        using var service = new DurableTaskHostedService(worker, orchestrationService, logger);
        await service.StopAsync(CancellationToken.None);
        worker.Dispose();
        logger.IsDisposed = disposeLogging;

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => service.StopAsync(CancellationToken.None))));

        logger.Errors.ShouldBeEmpty();
    }

    private sealed class ShutdownLogger : ILogger<DurableTaskHostedService>
    {
        public bool IsDisposed { get; set; }
        public ConcurrentQueue<Exception> Errors { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (logLevel >= LogLevel.Error && exception is not null)
            {
                Errors.Enqueue(exception);
            }
        }
    }
}

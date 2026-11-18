// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.DeId.Configuration;
using Ignixa.DeId.Models;
using Ignixa.DeId.Processors;
using Ignixa.DeId.Tests.Utilities;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ignixa.DeId.Tests.Pipeline;

/// <summary>
/// A processor failure's message can quote the value it was parsing, so logs name the failure and its type
/// and never carry that message. The caller still receives the full error.
/// </summary>
public class ProcessorFailureLoggingTests
{
    private const string Sensitive = "Zebra-Sensitive-Value";

    private const string Patient =
        """{"resourceType":"Patient","id":"p1","name":[{"family":"Doe"}]}""";

    [Theory]
    [InlineData(ErrorHandlingMode.StopOnError)]
    [InlineData(ErrorHandlingMode.LogAndContinue)]
    public async Task GivenProcessorThatThrowsWithDataInItsMessage_WhenDeidentifying_ThenNoLogRecordCarriesTheMessage(ErrorHandlingMode mode)
    {
        // Arrange
        var logs = new CapturingLoggerProvider();
        var options = new DeIdOptions
        {
            FhirVersion = "R4",
            Processing = new ProcessingOptions { ErrorHandling = mode },
            Rules = [new FhirPathRule { Path = "Patient.name.family", Method = "THROWING" }]
        };
        var engine = DeIdTestHelpers.CreateEngine(options, configureServices: services =>
        {
            services.AddLogging(builder => builder.AddProvider(logs));
            services.AddKeyedSingleton<IDeIdProcessor>("THROWING", new ThrowingProcessor());
        });

        // Act
        await engine.DeidentifyAsync(Patient);

        // Assert
        logs.Messages.ShouldNotBeEmpty();
        logs.Messages.ShouldAllBe(message => !message.Contains(Sensitive));
        logs.Exceptions.ShouldBeEmpty();
    }

    private sealed class ThrowingProcessor : IDeIdProcessor
    {
        public ValueTask<Result<ProcessorResult>> ProcessAsync(
            ResourceJsonNode resource,
            IElement node,
            ProcessorContext context,
            CancellationToken cancellationToken) =>
            throw new FormatException($"The value '{Sensitive}' was not recognized.");
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages = [];
        private readonly List<Exception> _exceptions = [];

        public IReadOnlyList<string> Messages
        {
            get { lock (_messages) { return [.. _messages]; } }
        }

        public IReadOnlyList<Exception> Exceptions
        {
            get { lock (_messages) { return [.. _exceptions]; } }
        }

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(CapturingLoggerProvider owner) : ILogger
        {
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
                lock (owner._messages)
                {
                    owner._messages.Add(formatter(state, exception));
                    if (exception is not null)
                    {
                        owner._exceptions.Add(exception);
                    }
                }
            }
        }
    }
}

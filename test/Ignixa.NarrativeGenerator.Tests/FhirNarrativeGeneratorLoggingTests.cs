// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirPath.Evaluation;
using Ignixa.NarrativeGenerator.Engine;
using Ignixa.NarrativeGenerator.Engine.ScriptFunctions;
using Ignixa.NarrativeGenerator.Security;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Ignixa.NarrativeGenerator.Tests;

/// <summary>
/// Pins that a logger supplied to <see cref="FhirNarrativeGenerator.Create"/> actually reaches
/// <see cref="FhirPathScriptFunctions"/>'s catch-block warnings during a real
/// <see cref="INarrativeGenerator.GenerateNarrativeAsync"/> render.
/// </summary>
/// <remarks>
/// <see cref="FhirPathScriptFunctionsErrorReportingTests"/> proves the five catch blocks log correctly
/// when <see cref="FhirPathScriptFunctions"/> is called directly. That does not prove the logger given
/// to <c>Create</c> ever reaches those catch blocks through an actual narrative render -- which is
/// exactly what the DI registration bug broke: it never passed a logger to <c>Create</c> at all, so
/// this path silently used <c>NullLogger</c> in production.
///
/// <c>Create</c> always resolves its own embedded, curated templates, none of which contain a
/// deliberately broken FHIRPath expression, so this test reaches the same internal constructor
/// <c>Create</c> uses (<see cref="FhirPathScriptFunctions"/>, <see cref="NarrativeTemplateEngine"/>,
/// <see cref="XhtmlSanitizer"/>) with a fake <see cref="ITemplateResolver"/> that hands back one.
/// Everything downstream of that -- the script functions, the template engine, the logger threading --
/// is the exact production code <c>Create</c> assembles.
/// </remarks>
public class FhirNarrativeGeneratorLoggingTests
{
    /// <summary>
    /// Parses cleanly, then fails during evaluation: '&amp;' is a singleton operator and the left
    /// operand is a three-item collection. Same expression <see cref="FhirPathScriptFunctionsErrorReportingTests"/>
    /// uses to exercise the identical catch block.
    /// </summary>
    private const string ThrowsAtEvaluationTime = "(1 | 2 | 3) & 'b'";

    private const string TemplateWithABrokenExpression = """{{ path resource "(1 | 2 | 3) & 'b'" }}""";

    [Fact]
    public async Task GivenARealLoggerSuppliedToCreate_WhenATemplateExpressionFailsToEvaluate_ThenTheWarningReachesIt()
    {
        // Arrange
        var schema = new R4CoreSchemaProvider();
        var recordingLogger = new RecordingLogger();
        var resolver = new SingleTemplateResolver(TemplateWithABrokenExpression);

        var fhirPathFunctions = new FhirPathScriptFunctions(schema, recordingLogger);
        var templateEngine = new NarrativeTemplateEngine(fhirPathFunctions, null, resolver);
        INarrativeGenerator generator = new FhirNarrativeGenerator(resolver, templateEngine, new XhtmlSanitizer(), schema);

        var patient = PatientBuilderFactory.Create(schema)
            .WithGivenName("Ada")
            .Build()
            .ToElement(schema);

        // Act
        var narrative = await generator.GenerateNarrativeAsync(patient, "Patient");

        // Assert: the expression fails, so the template renders blank for that call -- exactly the
        // scenario a decorative fix leaves indistinguishable from "matched nothing" -- but the failure
        // must still surface as a Warning through the logger Create() was given.
        narrative.ShouldBeEmpty();
        var entry = recordingLogger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain(ThrowsAtEvaluationTime);
        entry.Exception.ShouldNotBeNull();
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class RecordingLogger : ILogger
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
            ArgumentNullException.ThrowIfNull(formatter);
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed class SingleTemplateResolver(string content) : ITemplateResolver
    {
        public Task<TemplateResolution?> ResolveTemplateAsync(
            string resourceType,
            FhirVersion fhirVersion,
            TemplateFormat format,
            CancellationToken cancellationToken) =>
            Task.FromResult<TemplateResolution?>(new TemplateResolution(content, "Test/Broken", resourceType, fhirVersion, format, IsGenericFallback: false));

        public bool HasTemplate(string resourceType, FhirVersion fhirVersion, TemplateFormat format) => true;

        public Task<string?> ResolveDatatypeTemplateAsync(string datatypeName, TemplateFormat format, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<string?> ResolveByPathAsync(string templatePath, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }
}

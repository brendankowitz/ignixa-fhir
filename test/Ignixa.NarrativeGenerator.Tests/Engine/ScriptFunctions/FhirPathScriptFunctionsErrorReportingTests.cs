// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirPath.Evaluation;
using Ignixa.NarrativeGenerator.Engine.ScriptFunctions;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Ignixa.NarrativeGenerator.Tests.Engine.ScriptFunctions;

/// <summary>
/// Tests that a template expression which fails to evaluate is reported rather than swallowed.
/// </summary>
/// <remarks>
/// These functions deliberately do not propagate: one bad expression must not fail the whole narrative.
/// But their fallbacks - empty string, empty sequence, null, false, 0 - are exactly what a correct
/// expression that matched nothing returns, so without a log an ill-formed template renders blank
/// forever and nobody finds out.
/// </remarks>
public class FhirPathScriptFunctionsErrorReportingTests
{
    /// <summary>
    /// Parses cleanly, then fails during evaluation: '&amp;' is a singleton operator and the left
    /// operand is a three-item collection.
    /// </summary>
    private const string ThrowsAtEvaluationTime = "(1 | 2 | 3) & 'b'";

    private readonly R4CoreSchemaProvider _schemaProvider = new();
    private readonly RecordingLogger _logger = new();
    private readonly FhirPathScriptFunctions _functions;
    private readonly IElement _patient;

    public FhirPathScriptFunctionsErrorReportingTests()
    {
        _functions = new FhirPathScriptFunctions(_schemaProvider, _logger);
        _patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithGivenName("Ada")
            .Build()
            .ToElement(_schemaProvider);
    }

    [Fact]
    public void GivenAFailingExpression_WhenCallingPath_ThenReturnsEmptyAndLogsAWarning()
    {
        // Act
        var result = _functions.Path(_patient, ThrowsAtEvaluationTime);

        // Assert
        result.ShouldBeEmpty();
        ShouldHaveLoggedOneWarningMentioning(ThrowsAtEvaluationTime);
    }

    [Fact]
    public void GivenAFailingExpression_WhenCallingPathAll_ThenReturnsEmptyAndLogsAWarning()
    {
        // Act: also pins the materialization. PathAll used to return a lazy projection over a lazy
        // evaluation, so the throw happened at the template's first enumeration - past this catch.
        var result = _functions.PathAll(_patient, ThrowsAtEvaluationTime).ToList();

        // Assert
        result.ShouldBeEmpty();
        ShouldHaveLoggedOneWarningMentioning(ThrowsAtEvaluationTime);
    }

    [Fact]
    public void GivenAFailingExpression_WhenCallingPathElement_ThenReturnsNullAndLogsAWarning()
    {
        // Act
        var result = _functions.PathElement(_patient, ThrowsAtEvaluationTime);

        // Assert
        result.ShouldBeNull();
        ShouldHaveLoggedOneWarningMentioning(ThrowsAtEvaluationTime);
    }

    [Fact]
    public void GivenAFailingExpression_WhenCallingExists_ThenReturnsFalseAndLogsAWarning()
    {
        // Act
        var result = _functions.Exists(_patient, ThrowsAtEvaluationTime);

        // Assert
        result.ShouldBeFalse();
        ShouldHaveLoggedOneWarningMentioning(ThrowsAtEvaluationTime);
    }

    [Fact]
    public void GivenAFailingExpression_WhenCallingCount_ThenReturnsZeroAndLogsAWarning()
    {
        // Act
        var result = _functions.Count(_patient, ThrowsAtEvaluationTime);

        // Assert
        result.ShouldBe(0);
        ShouldHaveLoggedOneWarningMentioning(ThrowsAtEvaluationTime);
    }

    [Fact]
    public void GivenAnExpressionThatSimplyMatchesNothing_WhenCallingPath_ThenNothingIsLogged()
    {
        // Act: the case the log must stay quiet for, so a warning keeps meaning "something broke".
        var result = _functions.Path(_patient, "deceasedBoolean");

        // Assert
        result.ShouldBeEmpty();
        _logger.Entries.ShouldBeEmpty();
    }

    private void ShouldHaveLoggedOneWarningMentioning(string expression)
    {
        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain(expression);
        entry.Exception.ShouldBeOfType<FhirPathEvaluationException>();
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
}

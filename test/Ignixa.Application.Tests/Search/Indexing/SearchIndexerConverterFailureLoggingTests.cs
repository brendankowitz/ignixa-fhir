// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Indexing;

/// <summary>
/// <see cref="ElementSearchIndexer"/> deliberately contains any exception a converter or FHIRPath
/// evaluation raises, so one bad value never fails the whole write (see
/// <see cref="SearchIndexerFailureContainmentTests"/>). Before this fix, that containment logged
/// everything identically: a bad literal a converter is expected to reject, an unimplemented FHIRPath
/// function, and a genuine <see cref="NullReferenceException"/> in the converter's own code all became
/// the same Warning-level "skipping this value" line, indistinguishable from "matched nothing".
/// <para>
/// These tests pin that a converter bug is now logged differently from an expected rejection - at
/// <see cref="LogLevel.Error"/> instead of <see cref="LogLevel.Warning"/> - while the write still
/// survives it. They exercise <see cref="ElementSearchIndexer"/> directly with a fake converter
/// manager so the exception type reaching <c>ConvertOrLog</c> is fully controlled, rather than relying
/// on locating a real converter with a real defect.
/// </para>
/// </summary>
public class SearchIndexerConverterFailureLoggingTests
{
    private readonly IFhirSchemaProvider _schemaProvider = new R4CoreSchemaProvider();
    private readonly SearchParameterDefinitionManager _searchParameterDefinitionManager;

    public SearchIndexerConverterFailureLoggingTests()
    {
        _searchParameterDefinitionManager = new SearchParameterDefinitionManager(
            _schemaProvider,
            NullLogger<SearchParameterDefinitionManager>.Instance);
    }

    [Fact]
    public void GivenAConverterThatThrowsANullReferenceException_WhenIndexed_ThenTheFailureIsLoggedAtErrorNotWarning()
    {
        // Arrange - a NullReferenceException from a converter is a code defect, not a data-quality
        // problem. It must not be logged the same way as an expression the converter is designed to
        // reject.
        var recordingLogger = new RecordingLogger();
        var indexer = CreateIndexer(new NullReferenceException("simulated converter bug"), recordingLogger);
        var patient = PatientJson();
        var element = patient.ToElement(_schemaProvider);

        // Act
        var indices = Should.NotThrow(() => indexer.Extract(element));

        // Assert - contained (no entries for the parameters the throwing converter served), but the
        // log level is what makes this defect distinguishable from "the expression matched nothing".
        // Every non-composite Patient search parameter with an expression that produces a value here
        // (e.g. "gender", and the boolean-valued "deceased") routes through the same throwing
        // converter, so every recorded entry - not just one - must carry the defect classification.
        indices.Select(i => i.SearchParameter.Code).ShouldNotContain("gender");
        recordingLogger.Entries.ShouldNotBeEmpty();
        recordingLogger.Entries.ShouldAllBe(e => e.Level == LogLevel.Error);
        recordingLogger.Entries.ShouldAllBe(e => e.Exception is NullReferenceException);
    }

    [Fact]
    public void GivenAConverterThatThrowsAnInvalidCastException_WhenIndexed_ThenTheFailureIsLoggedAtErrorNotWarning()
    {
        // Arrange - same code-defect category as the NullReferenceException case above, different
        // exception type, same requirement: it must surface distinctly from an expected rejection.
        var recordingLogger = new RecordingLogger();
        var indexer = CreateIndexer(new InvalidCastException("simulated converter bug"), recordingLogger);
        var patient = PatientJson();
        var element = patient.ToElement(_schemaProvider);

        // Act
        var indices = Should.NotThrow(() => indexer.Extract(element));

        // Assert
        indices.Select(i => i.SearchParameter.Code).ShouldNotContain("gender");
        recordingLogger.Entries.ShouldNotBeEmpty();
        recordingLogger.Entries.ShouldAllBe(e => e.Level == LogLevel.Error);
        recordingLogger.Entries.ShouldAllBe(e => e.Exception is InvalidCastException);
    }

    [Fact]
    public void GivenAConverterThatThrowsANotSupportedException_WhenIndexed_ThenTheFailureIsStillLoggedAtWarning()
    {
        // Arrange - the control for the two tests above. An unimplemented FHIRPath function is an
        // expected containment case, not a defect, so it must keep logging at Warning exactly as
        // before. Without this control, the fix above could have silently reclassified every failure
        // as an Error rather than actually distinguishing causes.
        var recordingLogger = new RecordingLogger();
        var indexer = CreateIndexer(new NotSupportedException("simulated unimplemented function"), recordingLogger);
        var patient = PatientJson();
        var element = patient.ToElement(_schemaProvider);

        // Act
        var indices = Should.NotThrow(() => indexer.Extract(element));

        // Assert
        indices.Select(i => i.SearchParameter.Code).ShouldNotContain("gender");
        recordingLogger.Entries.ShouldNotBeEmpty();
        recordingLogger.Entries.ShouldAllBe(e => e.Level == LogLevel.Warning);
        recordingLogger.Entries.ShouldAllBe(e => e.Exception is NotSupportedException);
    }

    /// <summary>
    /// Builds an indexer whose converter manager always hands back a converter that throws
    /// <paramref name="toThrow"/>, regardless of which FHIR type or search parameter asked for it.
    /// </summary>
    private ElementSearchIndexer CreateIndexer(Exception toThrow, ILogger<ElementSearchIndexer> logger)
    {
        var referenceResolver = new NullReferenceToElementResolver();
        return new ElementSearchIndexer(
            new SupportedSearchParameterDefinitionManager(_searchParameterDefinitionManager),
            new AlwaysThrowingConverterManager(toThrow),
            referenceResolver,
            logger);
    }

    private static ResourceJsonNode PatientJson() => ResourceJsonNode.Parse("""
        {"resourceType":"Patient","id":"p1","gender":"male"}
        """);

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class RecordingLogger : ILogger<ElementSearchIndexer>
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

    private sealed class NullReferenceToElementResolver : IReferenceToElementResolver
    {
        public IElement Resolve(string reference) => null!;
    }

    private sealed class AlwaysThrowingConverterManager(Exception toThrow) : IElementToSearchValueConverterManager
    {
        public bool TryGetConverter(string fhirType, Type searchValueType, out IElementToSearchValueConverter converter)
        {
            converter = new ThrowingConverter(toThrow);
            return true;
        }
    }

    private sealed class ThrowingConverter(Exception toThrow) : IElementToSearchValueConverter
    {
        public IReadOnlyList<string> FhirTypes { get; } = ["code"];

        public Type SearchValueType => typeof(TokenSearchValue);

        public IEnumerable<ISearchValue> ConvertTo(IElement value) => throw toThrow;
    }
}

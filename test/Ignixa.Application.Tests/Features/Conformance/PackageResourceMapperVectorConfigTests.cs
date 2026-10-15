#nullable enable

using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using SearchParameterInfo = Ignixa.Application.Features.Conformance.SearchParameterInfo;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features.Conformance;

/// <summary>
/// Coverage for <see cref="PackageResourceMapper"/> parsing the <c>vector-search-config</c> extension
/// off a package-sourced SearchParameter JSON document and carrying it onto the conformance
/// <see cref="SearchParameterInfo"/> DTO - the first hop of the path that eventually reaches the Core
/// <see cref="Ignixa.Search.Models.SearchParameterInfo"/> built by
/// <see cref="Ignixa.Application.Features.Search.CompositeSearchParameterDefinitionManager"/>.
/// </summary>
public class PackageResourceMapperVectorConfigTests
{
    [Fact]
    public void GivenPackageJsonWithExtension_WhenMapped_ThenVectorConfigSurvivesToCoreInfo()
    {
        PackageResource resource = BuildSearchParameterResource(
            """
            {
              "resourceType": "SearchParameter",
              "id": "semantic-text",
              "url": "http://example.org/fhir/SearchParameter/semantic-text",
              "name": "semantic-text",
              "status": "active",
              "code": "semantic-text",
              "base": [ "Patient" ],
              "type": "special",
              "expression": "Patient.name.family",
              "extension": [
                {
                  "url": "http://microsoft.com/fhir/StructureDefinition/vector-search-config",
                  "extension": [
                    { "url": "extractionPolicy", "valueCode": "firstValue" },
                    { "url": "maxInputTokens", "valuePositiveInt": 2048 },
                    { "url": "minimumScore", "valueDecimal": 0.75 },
                    { "url": "chunkSizeTokens", "valuePositiveInt": 128 },
                    { "url": "chunkOverlapTokens", "valueUnsignedInt": 16 },
                    { "url": "distanceMetric", "valueCode": "cosine" }
                  ]
                }
              ]
            }
            """);

        PackageResources mapped = PackageResourceMapper.MapToPackageResources([resource]);

        SearchParameterInfo searchParameter = mapped.SearchParameters.ShouldHaveSingleItem();
        VectorSearchConfig vectorConfig = searchParameter.VectorConfig.ShouldNotBeNull();
        vectorConfig.ExtractionPolicy.ShouldBe(VectorTextExtractionPolicy.FirstValue);
        vectorConfig.MaxInputTokens.ShouldBe(2048);
        vectorConfig.MinimumScore.ShouldBe(0.75m);
        vectorConfig.ChunkSizeTokens.ShouldBe(128);
        vectorConfig.ChunkOverlapTokens.ShouldBe(16);
        searchParameter.Type.ShouldBe(SearchParamType.Special);
    }

    [Fact]
    public void GivenPackageJsonWithoutExtension_WhenMapped_ThenVectorConfigIsNull()
    {
        PackageResource resource = BuildSearchParameterResource(
            """
            {
              "resourceType": "SearchParameter",
              "id": "plain-token",
              "url": "http://example.org/fhir/SearchParameter/plain-token",
              "name": "plain-token",
              "status": "active",
              "code": "plain-token",
              "base": [ "Patient" ],
              "type": "token",
              "expression": "Patient.identifier"
            }
            """);

        PackageResources mapped = PackageResourceMapper.MapToPackageResources([resource]);

        SearchParameterInfo searchParameter = mapped.SearchParameters.ShouldHaveSingleItem();
        searchParameter.VectorConfig.ShouldBeNull();
        searchParameter.HasInvalidVectorConfig.ShouldBeFalse();
    }

    /// <summary>
    /// A malformed <c>vector-search-config</c> must not drop the whole SearchParameter from activation
    /// (the old behavior: <see cref="VectorSearchConfig.Parse(System.Text.Json.JsonElement)"/>'s
    /// <see cref="FormatException"/> fell into the method's blanket catch). It must keep every other
    /// field, flag <see cref="SearchParameterInfo.HasInvalidVectorConfig"/> so the Core
    /// <c>SearchParameterInfo.IsSupported</c> eventually goes false (see
    /// <c>CompositeSearchParameterDefinitionManagerVectorConfigTests</c>), and log a Warning naming
    /// the SearchParameter canonical and the parse failure reason.
    /// </summary>
    [Fact]
    public void GivenPackageJsonWithInvalidVectorConfig_WhenMapped_ThenParameterKeptAndWarningLogged()
    {
        PackageResource resource = BuildSearchParameterResource(
            """
            {
              "resourceType": "SearchParameter",
              "id": "semantic-text-bad",
              "url": "http://example.org/fhir/SearchParameter/semantic-text-bad",
              "name": "semantic-text-bad",
              "status": "active",
              "code": "semantic-text-bad",
              "base": [ "Patient" ],
              "type": "special",
              "expression": "Patient.name.family",
              "extension": [
                {
                  "url": "http://microsoft.com/fhir/StructureDefinition/vector-search-config",
                  "extension": [
                    { "url": "minimumScore", "valueDecimal": 1.5 }
                  ]
                }
              ]
            }
            """);
        var logger = new RecordingLogger();

        PackageResources mapped = PackageResourceMapper.MapToPackageResources([resource], logger);

        SearchParameterInfo searchParameter = mapped.SearchParameters.ShouldHaveSingleItem();
        searchParameter.VectorConfig.ShouldBeNull();
        searchParameter.HasInvalidVectorConfig.ShouldBeTrue();
        searchParameter.Code.ShouldBe("semantic-text-bad");
        searchParameter.Type.ShouldBe(SearchParamType.Special);

        RecordingLogger.LogEntry entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain(resource.Canonical);
        entry.Message.ShouldContain("minimumScore");
    }

    private static PackageResource BuildSearchParameterResource(string json) => new()
    {
        PackageId = "test.package",
        PackageVersion = "1.0.0",
        ResourceType = "SearchParameter",
        Canonical = "http://example.org/fhir/SearchParameter/semantic-text",
        ResourceId = "semantic-text",
        ResourceJson = json,
        FhirVersion = "4.0.1",
    };

    /// <summary>Minimal <see cref="ILogger"/> that records entries for assertion, matching the pattern
    /// used by <c>SearchIndexerConverterFailureLoggingTests.RecordingLogger</c>.</summary>
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

        public sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
    }
}

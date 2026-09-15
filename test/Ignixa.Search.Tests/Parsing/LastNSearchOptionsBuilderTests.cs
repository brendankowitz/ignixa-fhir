// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Specification.Generated;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Search.Tests.Parsing;

public class LastNSearchOptionsBuilderTests
{
    [Fact]
    public void GivenAnExplicitCount_WhenBuildingLastNOptions_ThenRecordsThatTheControlWasSpecified()
    {
        // Arrange
        var context = CreateObservationContext();
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        LastNSearchOptions options = builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
            new QueryParameter("code", "http://loinc.org|1234-5"),
            new QueryParameter("_count", "10"),
        ]);

        // Assert
        options.CountSpecified.ShouldBeTrue();
    }

    [Fact]
    public void GivenAnEmptyContinuationControl_WhenBuildingLastNOptions_ThenRecordsThatTheControlWasSpecified()
    {
        var context = CreateObservationContext();
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        LastNSearchOptions options = builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
            new QueryParameter("code", "http://loinc.org|1234-5"),
            new QueryParameter("after", string.Empty),
        ]);

        options.ContinuationSpecified.ShouldBeTrue();
    }

    [Fact]
    public void GivenNoMax_WhenBuildingLastNOptions_ThenDefaultsMaximumToOne()
    {
        // Arrange
        var context = CreateObservationContext();
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        LastNSearchOptions options = builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
            new QueryParameter("code", "http://loinc.org|1234-5"),
        ]);

        // Assert
        options.Maximum.ShouldBe(1);
        options.CodeParameter.Code.ShouldBe("code");
        options.EffectiveDateParameter.Code.ShouldBe("date");
    }

    [Fact]
    public void GivenAnExplicitPositiveMax_WhenBuildingLastNOptions_ThenUsesThatMaximum()
    {
        // Arrange
        var context = CreateObservationContext();
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        LastNSearchOptions options = builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
            new QueryParameter("code", "http://loinc.org|1234-5"),
            new QueryParameter("max", "3"),
        ]);

        // Assert
        options.Maximum.ShouldBe(3);
    }

    [Fact]
    public void GivenAZeroMax_WhenBuildingLastNOptions_ThenRejectsTheRequest()
    {
        // Arrange
        var context = CreateObservationContext();
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        BadSearchRequestException exception = Should.Throw<BadSearchRequestException>(() => builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
            new QueryParameter("code", "http://loinc.org|1234-5"),
            new QueryParameter("max", "0"),
        ]));

        // Assert
        exception.Message.ShouldContain("positive integer");
    }

    [Fact]
    public void GivenANegativeMax_WhenBuildingLastNOptions_ThenRejectsTheRequest()
    {
        // Arrange
        var context = CreateObservationContext();
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        BadSearchRequestException exception = Should.Throw<BadSearchRequestException>(() => builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
            new QueryParameter("code", "http://loinc.org|1234-5"),
            new QueryParameter("max", "-1"),
        ]));

        // Assert
        exception.Message.ShouldContain("positive integer");
    }

    [Fact]
    public void GivenAMalformedMax_WhenBuildingLastNOptions_ThenRejectsTheRequest()
    {
        // Arrange
        var context = CreateObservationContext();
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        BadSearchRequestException exception = Should.Throw<BadSearchRequestException>(() => builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
            new QueryParameter("code", "http://loinc.org|1234-5"),
            new QueryParameter("max", "many"),
        ]));

        // Assert
        exception.Message.ShouldContain("not a valid integer");
    }

    [Fact]
    public void GivenAMaxAboveTheServerLimit_WhenBuildingLastNOptions_ThenRejectsTheRequest()
    {
        // Arrange
        var context = CreateObservationContext();
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        BadSearchRequestException exception = Should.Throw<BadSearchRequestException>(() => builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
            new QueryParameter("code", "http://loinc.org|1234-5"),
            new QueryParameter("max", "1001"),
        ]));

        // Assert
        exception.Message.ShouldContain("1000");
    }

    [Fact]
    public void GivenDuplicateMaxParameters_WhenBuildingLastNOptions_ThenRejectsTheRequest()
    {
        // Arrange
        var context = CreateObservationContext();
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        BadSearchRequestException exception = Should.Throw<BadSearchRequestException>(() => builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
            new QueryParameter("code", "http://loinc.org|1234-5"),
            new QueryParameter("max", "1"),
            new QueryParameter("max", "2"),
        ]));

        // Assert
        exception.Message.ShouldContain("more than once");
    }

    [Fact]
    public void GivenNoSubjectFilterForR4_WhenBuildingLastNOptions_ThenRejectsTheRequest()
    {
        // Arrange
        var context = CreateObservationContext();
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        BadSearchRequestException exception = Should.Throw<BadSearchRequestException>(() => builder.Build(
        [
            new QueryParameter("code", "http://loinc.org|1234-5"),
        ]));

        // Assert
        exception.Message.ShouldContain("subject");
    }

    [Fact]
    public void GivenASubjectTypeModifier_WhenBuildingLastNOptions_ThenTreatsItAsTheRequiredSubjectFilter()
    {
        // Arrange
        var context = CreateObservationContext();
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        LastNSearchOptions options = builder.Build(
        [
            new QueryParameter("subject:Patient", "Patient/1"),
            new QueryParameter("code", "http://loinc.org|1234-5"),
        ]);

        // Assert
        options.Filters.Expression.ShouldNotBeNull();
    }

    [Fact]
    public void GivenNoSubjectFilterForR4B_WhenBuildingLastNOptions_ThenRejectsTheRequest()
    {
        // Arrange
        var context = CreateObservationContext(new R4BCoreSchemaProvider());
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        BadSearchRequestException exception = Should.Throw<BadSearchRequestException>(() => builder.Build(
        [
            new QueryParameter("code", "http://loinc.org|1234-5"),
        ]));

        // Assert
        exception.Message.ShouldContain("subject");
    }

    [Fact]
    public void GivenNoSubjectFilterForR5_WhenBuildingLastNOptions_ThenRejectsTheRequest()
    {
        // Arrange
        var context = CreateObservationContext(new R5CoreSchemaProvider());
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        BadSearchRequestException exception = Should.Throw<BadSearchRequestException>(() => builder.Build(
        [
            new QueryParameter("code", "http://loinc.org|1234-5"),
        ]));

        // Assert
        exception.Message.ShouldContain("subject");
    }

    [Fact]
    public void GivenNoSubjectFilterForR6_WhenBuildingLastNOptions_ThenRejectsTheRequest()
    {
        // Arrange
        var context = CreateObservationContext(new R6CoreSchemaProvider());
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        BadSearchRequestException exception = Should.Throw<BadSearchRequestException>(() => builder.Build(
        [
            new QueryParameter("code", "http://loinc.org|1234-5"),
        ]));

        // Assert
        exception.Message.ShouldContain("subject");
    }

    [Fact]
    public void GivenNoCategoryOrCodeBearingFilterForR6_WhenBuildingLastNOptions_ThenRejectsTheRequest()
    {
        // Arrange
        var context = CreateObservationContext(new R6CoreSchemaProvider());
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        BadSearchRequestException exception = Should.Throw<BadSearchRequestException>(() => builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
        ]));

        // Assert
        exception.Message.ShouldContain("category");
    }

    [Fact]
    public void GivenAnUnknownFutureFhirVersion_WhenBuildingLastNOptions_ThenRejectsRatherThanWeakeningValidation()
    {
        // Arrange
        var searchOptionsBuilder = Substitute.For<ISearchOptionsBuilder>();
        searchOptionsBuilder
            .Build(
                "Observation",
                Arg.Any<IReadOnlyList<QueryParameter>>(),
                Arg.Any<ISchema>(),
                Arg.Any<IList<ParameterTrace>>())
            .Returns(new SearchOptions());
        var definitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        definitionManager.GetSearchParameter("Observation", "code").Returns(
            new SearchParameterInfo(
                "code",
                "code",
                SearchParamType.Token,
                new Uri("http://hl7.org/fhir/SearchParameter/Observation-code")));
        definitionManager.GetSearchParameter("Observation", "date").Returns(
            new SearchParameterInfo(
                "date",
                "date",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/Observation-date")));
        var schemaProvider = Substitute.For<IFhirSchemaProvider>();
        schemaProvider.Version.Returns((FhirVersion)70);
        var builder = new LastNSearchOptionsBuilder(
            searchOptionsBuilder,
            definitionManager,
            schemaProvider);

        // Act
        NotSupportedException exception = Should.Throw<NotSupportedException>(() => builder.Build([]));

        // Assert
        exception.Message.ShouldContain("70");
    }

    [Fact]
    public void GivenNoCategoryOrCodeBearingFilterForR4_WhenBuildingLastNOptions_ThenRejectsTheRequest()
    {
        // Arrange
        var context = CreateObservationContext();
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        BadSearchRequestException exception = Should.Throw<BadSearchRequestException>(() => builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
        ]));

        // Assert
        exception.Message.ShouldContain("category");
    }

    [Fact]
    public void GivenACategoryFilterForR4_WhenBuildingLastNOptions_ThenAcceptsTheRequest()
    {
        // Arrange
        var context = CreateObservationContext();
        context.Add("Observation", "category", SearchParamType.Token, expression: "Observation.category");
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        LastNSearchOptions options = builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
            new QueryParameter("category", "laboratory"),
        ]);

        // Assert
        options.Maximum.ShouldBe(1);
    }

    [Theory]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenACustomPrimitiveCodeFilterWithoutACodePathSegment_WhenBuildingLastNOptions_ThenAcceptsTheRequest(
        FhirVersion version)
    {
        // Arrange
        var context = CreateObservationContext(CreateSchemaProvider(version));
        context.Add(
            "Observation",
            "workflow-status",
            SearchParamType.Token,
            expression: "Observation.status");
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        LastNSearchOptions options = builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
            new QueryParameter("workflow-status", "final"),
        ]);

        // Assert
        options.Maximum.ShouldBe(1);
    }

    [Theory]
    [InlineData(FhirVersion.R4, "Observation.code", "direct-concept")]
    [InlineData(FhirVersion.R4B, "Observation.code.coding", "direct-coding")]
    [InlineData(FhirVersion.R5, "Observation.code", "direct-concept")]
    [InlineData(FhirVersion.R6, "Observation.code.coding", "direct-coding")]
    public void GivenADirectComplexCodeBearingResult_WhenBuildingLastNOptions_ThenAcceptsTheRequest(
        FhirVersion version,
        string expression,
        string parameterCode)
    {
        // Arrange
        var context = CreateObservationContext(CreateSchemaProvider(version));
        context.Add("Observation", parameterCode, SearchParamType.Token, expression: expression);
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        LastNSearchOptions options = builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
            new QueryParameter(parameterCode, "http://loinc.org|1234-5"),
        ]);

        // Assert
        options.Maximum.ShouldBe(1);
    }

    [Fact]
    public void GivenACodeValueConceptComposite_WhenBuildingLastNOptions_ThenTreatsItsCodeComponentAsCodeBearing()
    {
        // Arrange
        var schemaProvider = new R4CoreSchemaProvider();
        var definitionManager = new SearchParameterDefinitionManager(
            schemaProvider,
            NullLogger<SearchParameterDefinitionManager>.Instance);
        var valueParser = new SearchParameterExpressionParser(
            new ReferenceSearchValueParser(schemaProvider, NullFhirBaseUriProvider.Instance),
            schemaProvider);
        var parser = new ExpressionParser(() => definitionManager, valueParser, schemaProvider);
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(parser, definitionManager),
            definitionManager,
            schemaProvider);

        // Act
        LastNSearchOptions options = builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
            new QueryParameter("code-value-concept", "http://loinc.org|1234-5$http://snomed.info/sct|123"),
        ]);

        // Assert
        options.Maximum.ShouldBe(1);
    }

    [Fact]
    public void GivenACompositeWithRelativeComponents_WhenBuildingLastNOptions_ThenAnalyzesTheCompositeFocus()
    {
        var context = CreateObservationContext();
        SearchParameterInfo type = context.Add(
            "Observation", "range-type", SearchParamType.Token, expression: "Observation.referenceRange.type");
        SearchParameterInfo low = context.Add(
            "Observation", "range-low", SearchParamType.Quantity, expression: "Observation.referenceRange.low");
        context.Add(
            "Observation",
            "range-type-low",
            SearchParamType.Composite,
            components:
            [
                new(type.Url, "type") { ResolvedSearchParameter = type },
                new(low.Url, "low") { ResolvedSearchParameter = low },
            ],
            expression: "Observation.referenceRange");
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        LastNSearchOptions options = builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
            new QueryParameter("range-type-low", "normal$5"),
        ]);

        options.Maximum.ShouldBe(1);
    }

    [Fact]
    public void GivenACompositeCodeableConceptResultWithoutACodePathSegment_WhenBuildingLastNOptions_ThenAcceptsTheRequest()
    {
        // Arrange
        var context = CreateObservationContext();
        context.Add(
            "Observation",
            "combo-value-concept",
            SearchParamType.Token,
            expression: "(Observation.value as CodeableConcept) | (Observation.component.value as CodeableConcept)");
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        LastNSearchOptions options = builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
            new QueryParameter("combo-value-concept", "http://snomed.info/sct|123"),
        ]);

        // Assert
        options.Maximum.ShouldBe(1);
    }

    [Theory]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenAnExpressionThatTraversesCodeButReturnsDisplay_WhenBuildingLastNOptions_ThenRejectsTheRequest(
        FhirVersion version)
    {
        // Arrange
        var context = CreateObservationContext(CreateSchemaProvider(version));
        context.Add(
            "Observation",
            "code-display",
            SearchParamType.Token,
            expression: "Observation.code.coding.display");
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        BadSearchRequestException exception = Should.Throw<BadSearchRequestException>(() => builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
            new QueryParameter("code-display", "display"),
        ]));

        // Assert
        exception.Message.ShouldContain("category");
    }

    [Fact]
    public void GivenAnUnsupportedSubjectModifier_WhenBuildingLastNOptions_ThenRejectsTheRequest()
    {
        // Arrange
        var context = CreateObservationContext();
        context.Add("Observation", "category", SearchParamType.Token, expression: "Observation.category");
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        BadSearchRequestException exception = Should.Throw<BadSearchRequestException>(() => builder.Build(
        [
            new QueryParameter("subject:unsupported", "Patient/1"),
            new QueryParameter("category", "laboratory"),
        ]));

        // Assert
        exception.Message.ShouldContain("subject");
    }

    [Fact]
    public void GivenAnUnsupportedCategoryModifier_WhenBuildingLastNOptions_ThenRejectsTheRequest()
    {
        // Arrange
        var context = CreateObservationContext();
        context.Add("Observation", "category", SearchParamType.Token, expression: "Observation.category");
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        BadSearchRequestException exception = Should.Throw<BadSearchRequestException>(() => builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
            new QueryParameter("category:unsupported", "laboratory"),
        ]));

        // Assert
        exception.Message.ShouldContain("category");
    }

    [Fact]
    public void GivenAnUnknownParameterBeforeCode_WhenBuildingLastNOptions_ThenPreservesOrdinaryParserHandling()
    {
        // Arrange
        var context = CreateObservationContext();
        context.DefinitionManager.GetSearchParameter("Observation", "unknown")
            .Returns(_ => throw new SearchParameterNotSupportedException("Observation", "unknown"));
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        LastNSearchOptions options = builder.Build(
        [
            new QueryParameter("unknown", "ignored"),
            new QueryParameter("subject", "Patient/1"),
            new QueryParameter("code", "http://loinc.org|1234-5"),
        ]);

        // Assert
        options.Filters.UnsupportedParams.ShouldContain("unknown");
    }

    [Fact]
    public void GivenAnOrdinaryObservationFilter_WhenBuildingLastNOptions_ThenPreservesItWithoutTreatingMaxAsSearch()
    {
        // Arrange
        var context = CreateObservationContext();
        context.Add("Observation", "status", SearchParamType.Token, expression: "Observation.status");
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        LastNSearchOptions options = builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
            new QueryParameter("code", "http://loinc.org|1234-5"),
            new QueryParameter("status", "final"),
            new QueryParameter("max", "2"),
        ]);

        // Assert
        options.Filters.Expression.ShouldNotBeNull();
        options.Filters.Expression.ToString().ShouldContain("status");
        options.Filters.UnsupportedParams.ShouldNotContain("max");
    }

    [Fact]
    public void GivenAMaxParameter_WhenValidatingCodeBearingFilters_ThenDoesNotResolveMaxAsAnObservationSearchParameter()
    {
        // Arrange
        var context = CreateObservationContext();
        context.DefinitionManager.GetSearchParameter("Observation", "max")
            .Returns(_ => throw new InvalidOperationException("max is not an Observation search parameter."));
        var builder = new LastNSearchOptionsBuilder(
            new SearchOptionsBuilder(context.Parser, context.DefinitionManager),
            context.DefinitionManager,
            context.SchemaProvider);

        // Act
        LastNSearchOptions options = builder.Build(
        [
            new QueryParameter("subject", "Patient/1"),
            new QueryParameter("max", "2"),
            new QueryParameter("code", "http://loinc.org|1234-5"),
        ]);

        // Assert
        options.Maximum.ShouldBe(2);
    }

    private static SearchParserTestContext CreateObservationContext(IFhirSchemaProvider? schemaProvider = null)
    {
        var context = schemaProvider is null
            ? new SearchParserTestContext()
            : new SearchParserTestContext(schemaProvider);
        context.Add("Observation", "subject", SearchParamType.Reference, targets: ["Patient"]);
        context.Add("Observation", "code", SearchParamType.Token, expression: "Observation.code");
        context.Add("Observation", "date", SearchParamType.Date, expression: "Observation.effective");
        return context;
    }

    private static IFhirSchemaProvider CreateSchemaProvider(FhirVersion version)
        => version switch
        {
            FhirVersion.R4 => new R4CoreSchemaProvider(),
            FhirVersion.R4B => new R4BCoreSchemaProvider(),
            FhirVersion.R5 => new R5CoreSchemaProvider(),
            FhirVersion.R6 => new R6CoreSchemaProvider(),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, null),
        };
}

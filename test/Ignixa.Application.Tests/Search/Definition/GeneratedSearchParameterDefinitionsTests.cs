// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa. All rights reserved.
// Licensed under the MIT License (MIT).
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Search.Definition;

/// <summary>
/// Guards the generated base search parameter sets against the FHIR specification's own *example*
/// SearchParameter instances leaking into the conformance set.
/// </summary>
/// <remarks>
/// The published search-parameters bundle ships three illustrative instances alongside the normative
/// ones — <c>SearchParameter/example</c>, <c>SearchParameter/example-reference</c> and
/// <c>SearchParameter/example-extension</c>. They are documentation, not conformance, but they declare
/// real codes on real base resource types, so they collide with normative entries.
/// <para>
/// The collision is decided by emission order, silently:
/// <see cref="SearchParameterDefinitionManager"/> populates both its URL and its type lookup with
/// <c>TryAdd</c>, so the first parameter emitted for a given (base resource type, code) pair wins and
/// every later one is discarded without a diagnostic. Before these entries were excluded from
/// generation, <c>SearchParameter/example</c> was emitted ahead of <c>Resource-id</c> and therefore
/// owned <c>_id</c> in every FHIR version — with expression <c>id</c> rather than <c>Resource.id</c>.
/// </para>
/// </remarks>
public class GeneratedSearchParameterDefinitionsTests
{
    /// <summary>
    /// Canonical URLs of the specification's example SearchParameter instances. None of these may
    /// appear in a generated conformance set.
    /// </summary>
    private static readonly string[] ExampleUrls =
    [
        "http://hl7.org/fhir/SearchParameter/example",
        "http://hl7.org/fhir/SearchParameter/example-reference",
        "http://hl7.org/fhir/SearchParameter/example-extension",
    ];

    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenAGeneratedDefinitionSet_WhenLoaded_ThenIdResolvesToTheNormativeResourceIdParameter(FhirVersion version)
    {
        // Arrange
        var manager = CreateManager(version);

        // Act
        var found = manager.TryGetSearchParameter("Patient", "_id", out var parameter);

        // Assert: SearchParameter/example also declares code _id on Resource, with expression "id".
        // If it wins the TryAdd race, every _id search compiles against the wrong expression.
        found.ShouldBeTrue();
        parameter.Url.ShouldBe(new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        parameter.Expression.ShouldBe("Resource.id");
    }

    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenAGeneratedDefinitionSet_WhenLoaded_ThenNoExampleSearchParameterIsPresent(FhirVersion version)
    {
        // Arrange
        var manager = CreateManager(version);

        // Act
        var present = ExampleUrls
            .Where(url => manager.UrlLookup.ContainsKey(new Uri(url)))
            .ToList();

        // Assert
        present.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenAGeneratedDefinitionSet_WhenLoaded_ThenSubjectOnConditionResolvesToTheNormativeParameter(FhirVersion version)
    {
        // Arrange
        var manager = CreateManager(version);

        // Act
        var found = manager.TryGetSearchParameter("Condition", "subject", out var parameter);

        // Assert: SearchParameter/example-reference declares code "subject" on Condition targeting
        // Organization, while the normative Condition-subject targets the patient/group types — so
        // adopting the example silently changes which references a chain may traverse. Before the
        // exclusion this resolved correctly only because Condition-subject happened to be emitted
        // first; this assertion removes the reliance on emission order.
        found.ShouldBeTrue();
        parameter.Url.ShouldBe(new Uri("http://hl7.org/fhir/SearchParameter/Condition-subject"));
    }

    private static SearchParameterDefinitionManager CreateManager(FhirVersion version)
    {
        IFhirSchemaProvider schema = version switch
        {
            FhirVersion.Stu3 => new STU3CoreSchemaProvider(),
            FhirVersion.R4 => new R4CoreSchemaProvider(),
            FhirVersion.R4B => new R4BCoreSchemaProvider(),
            FhirVersion.R5 => new R5CoreSchemaProvider(),
            FhirVersion.R6 => new R6CoreSchemaProvider(),
            _ => throw new NotSupportedException($"FHIR version {version} is not covered by this test."),
        };

        return new SearchParameterDefinitionManager(schema, NullLogger<SearchParameterDefinitionManager>.Instance);
    }
}

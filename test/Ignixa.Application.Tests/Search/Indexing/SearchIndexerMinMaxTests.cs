// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Globalization;
using Shouldly;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ignixa.Abstractions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Definition;
using Ignixa.Specification.Generated;
using Ignixa.Serialization.SourceNodes;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.Application.Tests.Search.Indexing;

public class SearchIndexerMinMaxTests
{
    private readonly R4CoreSchemaProvider _schemaProvider;
    private readonly ISearchIndexer _indexer;

    public SearchIndexerMinMaxTests()
    {
        _schemaProvider = new R4CoreSchemaProvider();
        var loggerFactory = NullLoggerFactory.Instance;

        var searchParamManager = new SearchParameterDefinitionManager(
            _schemaProvider,
            new NullLogger<SearchParameterDefinitionManager>());

        _indexer = SearchIndexerFactory.CreateInstance(
            _schemaProvider,
            loggerFactory,
            searchParamManager,
            NullFhirBaseUriProvider.Instance);
    }

    [Fact]
    public void GivenAPatientWithTwoDistinctNames_WhenIndexed_ThenExactlyOneNameValueIsMarkedMinAndOneIsMarkedMax()
    {
        // Arrange -- two distinct HumanName entries produce multiple "name" search values
        // (String type, multi-valued) -- exactly the shape MarkMinMaxValues exists to flag.
        var patient = PatientBuilderFactory.Create(_schemaProvider, seed: 42)
            .WithFamilyName("Zorro")
            .AddName("Adams", "Anna")
            .Build();

        var element = patient.ToElement(_schemaProvider);

        // Act
        var indices = _indexer.Extract(element);

        // Assert
        var nameValues = indices
            .Where(i => i.SearchParameter.Code == "name")
            .Select(i => i.Value)
            .OfType<StringSearchValue>()
            .ToList();

        nameValues.Count.ShouldBeGreaterThan(1); // multiple values extracted for a multi-name patient

        var minMarked = nameValues.Where(v => v.IsMin).ToList();
        var maxMarked = nameValues.Where(v => v.IsMax).ToList();

        minMarked.Count.ShouldBe(1);
        maxMarked.Count.ShouldBe(1);

        // Matches StringSearchValue.CompareTo's real comparison (case- and accent-insensitive,
        // invariant culture) so the test validates against the actual production comparer.
#pragma warning disable CA1309
        var productionComparer = Comparer<string>.Create((a, b) =>
            string.Compare(a, b, CultureInfo.InvariantCulture, CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreCase));
#pragma warning restore CA1309

        var expectedMin = nameValues.MinBy(v => v.String, productionComparer);
        var expectedMax = nameValues.MaxBy(v => v.String, productionComparer);

        minMarked[0].String.ShouldBe(expectedMin!.String);
        maxMarked[0].String.ShouldBe(expectedMax!.String);
    }

    [Fact]
    public void GivenAPatientWithOneFamilyName_WhenIndexed_ThenTheSoleFamilyNameValueIsMarkedBothMinAndMax()
    {
        // Arrange -- a single value for a search parameter is trivially both its own min and max
        // (fhir-server's own documented behavior for this case).
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithFamilyName("OnlyFamily")
            .Build();

        var element = patient.ToElement(_schemaProvider);

        // Act
        var indices = _indexer.Extract(element);

        // Assert
        var familyValues = indices
            .Where(i => i.SearchParameter.Code == "family")
            .Select(i => i.Value)
            .OfType<StringSearchValue>()
            .ToList();

        familyValues.Count.ShouldBe(1);
        familyValues[0].IsMin.ShouldBeTrue();
        familyValues[0].IsMax.ShouldBeTrue();
    }
}

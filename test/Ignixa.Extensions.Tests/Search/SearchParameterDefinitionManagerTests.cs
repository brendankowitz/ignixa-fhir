// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ignixa.Domain;
using Ignixa.SourceNodeSerialization;
using Ignixa.Search.Definition;
using Ignixa.Specification.Schema;
using Xunit;

namespace Ignixa.Extensions.Tests.Search;

public class SearchParameterDefinitionManagerTests
{
    private readonly ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

    [Fact]
    public async Task Start_WithR4Schema_ShouldLoadSearchParametersSuccessfully()
    {
        // Arrange
        var schemaProvider = new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R4);
        var manager = new SearchParameterDefinitionManager(
            schemaProvider,
            _loggerFactory.CreateLogger<SearchParameterDefinitionManager>());

        // Act
        try
        {
            await manager.Start();

            // Assert
            Assert.NotEmpty(manager.AllSearchParameters);
            Assert.True(manager.AllSearchParameters.Count() > 100, "Should load at least 100 search parameters");
        }
        catch (Sparky.Extensions.Exceptions.InvalidDefinitionException ex)
        {
            // Output the actual validation issues to help debug
            var issuesDetails = string.Join("\n", ex.Issues.Select(i => $"  - [{i.Severity}] {i.Diagnostics}"));
            throw new Exception($"Search parameter validation failed with {ex.Issues.Count} issue(s):\n{issuesDetails}", ex);
        }
    }

    [Fact]
    public async Task Start_WithR4Schema_ShouldLoadPatientSearchParameters()
    {
        // Arrange
        var schemaProvider = new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R4);
        var manager = new SearchParameterDefinitionManager(
            schemaProvider,
            _loggerFactory.CreateLogger<SearchParameterDefinitionManager>());

        // Act
        await manager.Start();

        // Assert
        var patientSearchParams = manager.GetSearchParameters("Patient");
        Assert.NotEmpty(patientSearchParams);

        // Common Patient search parameters
        Assert.True(manager.TryGetSearchParameter("Patient", "name", out var nameParam));
        Assert.NotNull(nameParam);
        Assert.Equal("name", nameParam.Code);

        Assert.True(manager.TryGetSearchParameter("Patient", "birthdate", out var birthdateParam));
        Assert.NotNull(birthdateParam);
        Assert.Equal("birthdate", birthdateParam.Code);

        Assert.True(manager.TryGetSearchParameter("Patient", "identifier", out var identifierParam));
        Assert.NotNull(identifierParam);
        Assert.Equal("identifier", identifierParam.Code);
    }

    [Fact]
    public async Task Start_WithR4Schema_ShouldLoadObservationSearchParameters()
    {
        // Arrange
        var schemaProvider = new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R4);
        var manager = new SearchParameterDefinitionManager(
            schemaProvider,
            _loggerFactory.CreateLogger<SearchParameterDefinitionManager>());

        // Act
        await manager.Start();

        // Assert
        var observationSearchParams = manager.GetSearchParameters("Observation");
        Assert.NotEmpty(observationSearchParams);

        // Common Observation search parameters
        Assert.True(manager.TryGetSearchParameter("Observation", "code", out var codeParam));
        Assert.NotNull(codeParam);
        Assert.Equal("code", codeParam.Code);

        Assert.True(manager.TryGetSearchParameter("Observation", "value-quantity", out var valueQuantityParam));
        Assert.NotNull(valueQuantityParam);
        Assert.Equal("value-quantity", valueQuantityParam.Code);

        Assert.True(manager.TryGetSearchParameter("Observation", "subject", out var subjectParam));
        Assert.NotNull(subjectParam);
        Assert.Equal("subject", subjectParam.Code);
    }

    [Fact]
    public async Task Start_WithR5Schema_ShouldLoadSearchParametersSuccessfully()
    {
        // Arrange
        var schemaProvider = new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R5);
        var manager = new SearchParameterDefinitionManager(
            schemaProvider,
            _loggerFactory.CreateLogger<SearchParameterDefinitionManager>());

        // Act
        await manager.Start();

        // Assert
        Assert.NotEmpty(manager.AllSearchParameters);
        Assert.True(manager.AllSearchParameters.Count() > 100, "Should load at least 100 search parameters");
    }

    [Fact]
    public async Task GetSearchParameter_WithValidResourceTypeAndCode_ShouldReturnParameter()
    {
        // Arrange
        var schemaProvider = new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R4);
        var manager = new SearchParameterDefinitionManager(
            schemaProvider,
            _loggerFactory.CreateLogger<SearchParameterDefinitionManager>());
        await manager.Start();

        // Act
        var parameter = manager.GetSearchParameter("Patient", "name");

        // Assert
        Assert.NotNull(parameter);
        Assert.Equal("name", parameter.Code);
        Assert.Equal("Patient", parameter.BaseResourceTypes.First());
    }

    [Fact]
    public async Task TryGetSearchParameter_WithInvalidCode_ShouldReturnFalse()
    {
        // Arrange
        var schemaProvider = new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R4);
        var manager = new SearchParameterDefinitionManager(
            schemaProvider,
            _loggerFactory.CreateLogger<SearchParameterDefinitionManager>());
        await manager.Start();

        // Act
        var found = manager.TryGetSearchParameter("Patient", "invalid-code-that-does-not-exist", out var parameter);

        // Assert
        Assert.False(found);
        Assert.Null(parameter);
    }
}

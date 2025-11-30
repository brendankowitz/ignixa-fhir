// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable disable

using FluentAssertions;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Operations.Features.Transform;
using Ignixa.Domain.Abstractions;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Registry;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Ignixa.Application.Tests.Features.Transform;

/// <summary>
/// Integration tests for TransformResourceHandler.
/// Tests the FHIR $transform operation error handling and parameter validation.
/// </summary>
public class TransformResourceHandlerTests
{
    private readonly IPackageResourceRepository _repository;
    private readonly IMapRegistry _mapRegistry;
    private readonly MappingParser _mappingParser;
    private readonly StructureMapParser _structureMapParser;
    private readonly IFhirVersionContext _versionContext;
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly ConceptMapResolverService _conceptMapService;
    private readonly TransformResourceHandler _handler;

    public TransformResourceHandlerTests()
    {
        _repository = Substitute.For<IPackageResourceRepository>();
        _mapRegistry = Substitute.For<IMapRegistry>();
        _mappingParser = new MappingParser();
        _structureMapParser = new StructureMapParser();

        // Use real R4 schema provider for Transform tests
        _versionContext = Substitute.For<IFhirVersionContext>();
        var r4Schema = new R4CoreSchemaProvider();
        _versionContext.GetSchemaProvider(FhirSpecification.R4, Arg.Any<int?>())
            .Returns(r4Schema);

        // Mock IFhirRequestContextAccessor with test context
        _contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        var mockContext = new FhirRequestContext
        {
            TenantId = 1,
            FhirVersion = FhirSpecification.R4
        };
        _contextAccessor.RequestContext.Returns(mockContext);

        // ConceptMapResolverService for translate() function support
        var mockTerminologyService = Substitute.For<ITerminologyService>();
        _conceptMapService = new ConceptMapResolverService(
            mockTerminologyService,
            NullLogger<ConceptMapResolverService>.Instance);

        // TransformResourceHandler now uses MapRegistryCache
        var mockMapCache = Substitute.For<MapRegistryCache>(
            _repository,
            _structureMapParser,
            NullLogger<MapRegistryCache>.Instance);

        // FhirPathEvaluatorWithTimeout needs real instances (can't mock due to constructor)
        var fhirPathParser = new Ignixa.FhirPath.Parser.FhirPathParser();
        var expressionCache = new FhirPathExpressionCache(
            fhirPathParser,
            NullLogger<FhirPathExpressionCache>.Instance);
        var fhirPathEvaluator = new Ignixa.FhirPath.Evaluation.FhirPathEvaluator();
        var fhirPathEvaluatorWithTimeout = new FhirPathEvaluatorWithTimeout(
            expressionCache,
            fhirPathEvaluator,
            TimeSpan.FromSeconds(5),
            NullLogger<FhirPathEvaluatorWithTimeout>.Instance);

        _handler = new TransformResourceHandler(
            mockMapCache,
            _mappingParser,
            _structureMapParser,
            _conceptMapService,
            fhirPathParser,
            fhirPathEvaluator,
            fhirPathEvaluatorWithTimeout,
            _versionContext,
            _contextAccessor,
            NullLogger<TransformResourceHandler>.Instance);
    }

    #region Error Handling Tests

    [Fact]
    public async Task GivenMissingContentParameter_WhenTransforming_ThenThrowsException()
    {
        // Arrange
        var validFml = "map 'http://example.org/test/Map' = 'TestMap' group Main(source src, target tgt) {}";

        var command = new TransformResourceCommand(
            SrcMaps: [validFml],
            Content: null); // Missing required parameter

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.HandleAsync(command, CancellationToken.None));

        exception.Message.Should().Contain("Content parameter is required");
    }

    [Fact]
    public async Task GivenNoMappingSource_WhenTransforming_ThenThrowsException()
    {
        // Arrange
        var sourcePatient = CreatePatient("patient-no-map", "Test", "User", "male", "2000-01-01");

        var command = new TransformResourceCommand(
            Source: null,
            SourceMap: null,
            SrcMaps: null,
            Content: sourcePatient);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.HandleAsync(command, CancellationToken.None));

        exception.Message.Should().Contain("No mapping source provided");
    }

    [Fact]
    public async Task GivenInvalidFmlText_WhenTransforming_ThenThrowsException()
    {
        // Arrange
        var invalidFml = "map 'invalid syntax here'";
        var sourcePatient = CreatePatient("patient-123", "Doe", "John", "male", "1990-01-15");

        var command = new TransformResourceCommand(
            SrcMaps: [invalidFml],
            Content: sourcePatient);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.HandleAsync(command, CancellationToken.None));
    }

    #endregion

    #region Mutation Verification Tests

    /// <summary>
    /// Integration test for Transform mutation strategy.
    /// Verifies that Transform operations actually mutate target resources (not just store variables).
    ///
    /// This test uses a real R4 schema provider to properly navigate source properties.
    /// See: docs/investigations/transform-mutation-strategy.md for implementation details.
    /// </summary>
    [Fact]
    public async Task GivenPatientSimplifyMap_WhenTransforming_ThenCopiesFieldsToTarget()
    {
        // Arrange - Create source patient with data
        var sourcePatientJson = """
            {
                "resourceType": "Patient",
                "id": "patient-123",
                "gender": "male",
                "birthDate": "1990-01-15",
                "active": true,
                "name": [{
                    "use": "official",
                    "family": "Doe",
                    "given": ["John", "Jacob"]
                }]
            }
            """;

        var sourcePatient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(sourcePatientJson);

        // FML that copies fields from source to target
        var fml = """
            map 'http://example.org/PatientSimplify' = 'PatientSimplify'

            uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
            uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias SimplePatient as target

            group Transform(source src : Patient, target tgt : SimplePatient) {
              src.id -> tgt.id;
              src.gender -> tgt.gender;
              src.birthDate -> tgt.birthDate;
              src.active -> tgt.active;
              src.name as vn -> tgt.name = vn;
            }
            """;

        var command = new TransformResourceCommand(
            Source: null,
            SourceMap: null,
            SrcMaps: [fml],
            SupportingMaps: null,
            Content: sourcePatient);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert - Verify target resource was actually mutated
        result.Should().NotBeNull("transform should return a result");
        result.ResourceType.Should().Be("Patient", "target should be a Patient resource");

        // Verify MutableNode has the copied properties
        result.MutableNode.Should().NotBeNull("MutableNode should be populated");

        // Check primitive values
        result.MutableNode!["id"]?.GetValue<string>().Should().Be("patient-123", "id should be copied");
        result.MutableNode["gender"]?.GetValue<string>().Should().Be("male", "gender should be copied");
        result.MutableNode["birthDate"]?.GetValue<string>().Should().Be("1990-01-15", "birthDate should be copied");
        result.MutableNode["active"]?.GetValue<bool>().Should().Be(true, "active should be copied");

        // Check complex object (name array)
        result.MutableNode["name"].Should().NotBeNull("name should be copied");
        var nameArray = result.MutableNode["name"]!.AsArray();
        nameArray.Should().HaveCount(1, "should have one name");

        var name = nameArray[0]!.AsObject();
        name["use"]?.GetValue<string>().Should().Be("official", "name.use should be copied");
        name["family"]?.GetValue<string>().Should().Be("Doe", "name.family should be copied");

        var givenArray = name["given"]!.AsArray();
        givenArray.Should().HaveCount(2, "should have two given names");
        givenArray[0]?.GetValue<string>().Should().Be("John");
        givenArray[1]?.GetValue<string>().Should().Be("Jacob");
    }

    /// <summary>
    /// Test that proves actual mutation is happening by adding literal values not present in source.
    /// If only variable storage was happening, these literal values would not appear in the result.
    /// </summary>
    [Fact]
    public async Task GivenPatientWithMissingFields_WhenTransforming_ThenAddsLiteralValues()
    {
        // Arrange - Create a minimal Patient (missing active and gender)
        var sourcePatientJson = """
            {
                "resourceType": "Patient",
                "id": "patient-456",
                "birthDate": "1990-01-01",
                "name": [{
                    "family": "Test"
                }]
            }
            """;

        var sourcePatient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(sourcePatientJson);

        // FML that copies existing fields AND adds literal values not in source
        var fml = """
            map 'http://example.org/PatientEnrich' = 'PatientEnrich'

            uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
            uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias EnrichedPatient as target

            group Transform(source src : Patient, target tgt : EnrichedPatient) {
              src.id -> tgt.id;
              src.birthDate -> tgt.birthDate;
              src.name -> tgt.name;
              src -> tgt.active = true "Add active flag";
              src -> tgt.gender = 'unknown' "Add default gender";
            }
            """;

        var command = new TransformResourceCommand(
            Source: null,
            SourceMap: null,
            SrcMaps: [fml],
            SupportingMaps: null,
            Content: sourcePatient);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert - Verify copied fields
        result.Should().NotBeNull();
        result.MutableNode!["id"]?.GetValue<string>().Should().Be("patient-456", "id should be copied");
        result.MutableNode["birthDate"]?.GetValue<string>().Should().Be("1990-01-01", "birthDate should be copied");
        result.MutableNode["name"].Should().NotBeNull("name should be copied");

        // CRITICAL: Verify literal values were added via mutation (not just stored in variables)
        result.MutableNode["active"]?.GetValue<bool>().Should().Be(true,
            "active literal value should be mutated into target - if this fails, mutation is not working");
        result.MutableNode["gender"]?.GetValue<string>().Should().Be("unknown",
            "gender literal value should be mutated into target - if this fails, mutation is not working");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a test Patient resource with specified fields.
    /// </summary>
    private ResourceJsonNode CreatePatient(string id, string family, string given, string gender, string birthDate)
    {
        var patientJson = $$"""
            {
              "resourceType": "Patient",
              "id": "{{id}}",
              "name": [
                {
                  "use": "official",
                  "family": "{{family}}",
                  "given": ["{{given}}"]
                }
              ],
              "gender": "{{gender}}",
              "birthDate": "{{birthDate}}"
            }
            """;

        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(patientJson);
    }

    #endregion
}

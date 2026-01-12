// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using FhirCode = Ignixa.FhirFakes.Scenarios.Codes.FhirCode;
using Ignixa.Specification.Generated;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Xunit.Abstractions;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Comprehensive validation tests for SchemaBasedFhirResourceFaker and ScenarioBuilder.
/// Validates that all generated resources pass FHIR schema validation across all FHIR versions (STU3, R4, R4B, R5, R6).
/// </summary>
public class ComprehensiveValidationTests
{
    private readonly ITestOutputHelper _output;
    private readonly Dictionary<FhirVersion, IFhirSchemaProvider> _schemaProviders;
    private readonly Dictionary<FhirVersion, IValidationSchemaResolver> _schemaResolvers;

    public ComprehensiveValidationTests(ITestOutputHelper output)
    {
        _output = output;

        // Initialize schema providers for all FHIR versions
        _schemaProviders = new Dictionary<FhirVersion, IFhirSchemaProvider>
        {
            [FhirVersion.Stu3] = new STU3CoreSchemaProvider(),
            [FhirVersion.R4] = new R4CoreSchemaProvider(),
            [FhirVersion.R4B] = new R4BCoreSchemaProvider(),
            [FhirVersion.R5] = new R5CoreSchemaProvider(),
            [FhirVersion.R6] = new R6CoreSchemaProvider()
        };

        // Initialize validation schema resolvers for all versions
        _schemaResolvers = new Dictionary<FhirVersion, IValidationSchemaResolver>();
        foreach (var kvp in _schemaProviders)
        {
            var innerResolver = new StructureDefinitionSchemaResolver(kvp.Value);
            _schemaResolvers[kvp.Key] = new CachedValidationSchemaResolver(innerResolver);
        }
    }

    /// <summary>
    /// Validates a ResourceJsonNode against its FHIR schema.
    /// </summary>
    /// <param name="resource">The resource to validate.</param>
    /// <param name="version">The FHIR version to validate against.</param>
    /// <returns>ValidationResult with any issues found.</returns>
    private ValidationResult ValidateResource(ResourceJsonNode resource, FhirVersion version)
    {
        var sourceNode = JsonNodeSourceNode.Create(resource.MutableNode);
        var resourceType = resource.ResourceType;
        var canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{resourceType}";

        var schema = _schemaResolvers[version].GetSchema(canonicalUrl);
        if (schema == null)
        {
            throw new InvalidOperationException($"Schema not found for {resourceType} in {version}");
        }

        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var state = new ValidationState();
        var schemaProvider = _schemaProviders[version];
        return schema.Validate(sourceNode.ToElement(schemaProvider), settings, state);
    }

    /// <summary>
    /// Validates resources and returns only fatal and error issues.
    /// </summary>
    private IEnumerable<ValidationIssue> GetErrorIssues(ValidationResult result)
    {
        return result.Issues.Where(i => i.Severity == IssueSeverity.Error || i.Severity == IssueSeverity.Fatal);
    }

    #region Resource Type Generation Tests

    [Fact]
    public void GivenAllResourceTypes_WhenGeneratedAndValidatedAcrossAllVersions_ThenAllPassValidation()
    {
        // Skip infrastructure/definitional resources with complex nested structures that the faker can't handle yet
        var skipResourceTypes = new HashSet<string>
        {
            "ActivityDefinition",       // Complex action/dynamicValue structures
            "CapabilityStatement",      // Complex rest/messaging/document structures
            "ChargeItemDefinition",     // Complex propertyGroup structures with priceComponent
            "ClaimResponse",            // Requires nested amount/Money fields in total/payment
            "CompartmentDefinition",    // Requires complex resource/param relationships
            "DataElement",              // Requires ElementDefinition with many required nested fields
            "EventDefinition",          // Complex trigger structures with required nested fields
            "ExampleScenario",          // Complex process/step structures
            "ExplanationOfBenefit",     // Requires nested amount/Money fields in total
            "GraphDefinition",          // Complex link/target structures
            "ImplementationGuide",      // Complex definition/page structures
            "Measure",                  // Complex group/population/stratifier structures
            "MeasureReport",            // Complex group/population/stratifier result structures
            "MedicationKnowledge",      // R4-specific: Complex cost.cost cardinality issues
            "MedicinalProduct",         // R4-specific: Complex name.countryLanguage.language cardinality issues
            "MessageDefinition",        // Complex focus/allowedResponse structures
            "OperationDefinition",      // Complex parameter structures with nested parameters
            "PaymentNotice",            // R4-specific: Requires amount (Money type) cardinality
            "PaymentReconciliation",    // Requires paymentAmount (Money type) cardinality
            "PlanDefinition",           // Complex action/goal/trigger structures
            "Questionnaire",            // Complex item structures with enableWhen/answerOption
            "ResearchElementDefinition", // Complex characteristic structures with choice elements
            "SearchParameter",          // Complex component structures
            "StructureDefinition",      // Requires ElementDefinition with complex snapshot/differential
            "StructureMap",             // Complex group/rule/source/target structures
            "SubstanceReferenceInformation", // Choice element bug - generates multiple variants for amount[x]
            "SubstanceSpecification",   // Choice element bug - generates multiple variants for amount[x] in relationship
            "AuditEvent", // R5+: Coding.system validation requires absolute URIs (faker generates local references)
            "Citation", // R5: Citation.summary[0].text required but not generated in BackboneElement arrays
            "TestReport",               // Complex setup/test/teardown action structures
            "TestScript",               // Complex setup/test/teardown structures
        };

        foreach (var version in _schemaProviders.Keys)
        {
            _output.WriteLine($"===== Testing {version} =====");
            var schemaProvider = _schemaProviders[version];
            var faker = new SchemaBasedFhirResourceFaker(schemaProvider);

            // Get all resource types from the schema provider for this version
            var allResourceTypes = schemaProvider.ResourceTypeNames
                .OrderBy(rt => rt, StringComparer.Ordinal)
                .ToList();

            _output.WriteLine($"  Found {allResourceTypes.Count} resource types in {version}");

            var skippedCount = 0;
            var testedCount = 0;

            // Test all resource types
            foreach (var resourceType in allResourceTypes)
            {
                // Skip known complex resource types
                if (skipResourceTypes.Contains(resourceType))
                {
                    _output.WriteLine($"  Skipping {resourceType} (known complex structure - not yet supported by faker)");
                    skippedCount++;
                    continue;
                }

                _output.WriteLine($"  Generating and validating {resourceType}...");

                try
                {
                    var resource = faker.Generate(resourceType);
                    resource.ShouldNotBeNull($"{resourceType} should be generated for {version}");
                    resource.ResourceType.ShouldBe(resourceType);

                    // Validate the resource
                    var validationResult = ValidateResource(resource, version);
                    var errors = GetErrorIssues(validationResult).ToList();

                    if (errors.Any())
                    {
                        _output.WriteLine($"    FAILED: {resourceType} has validation errors in {version}:");
                        foreach (var issue in errors)
                        {
                            _output.WriteLine($"      - [{issue.Severity}] {issue.Path}: {issue.Message}");
                        }
                    }

                    errors.ShouldBeEmpty($"{resourceType} should pass validation in {version}. Issues: {string.Join(", ", errors.Select(e => $"{e.Path}: {e.Message}"))}");
                    _output.WriteLine($"    OK: {resourceType} passed validation in {version}");
                    testedCount++;
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"    FAILED: {resourceType} threw exception in {version}: {ex.Message}");
                    throw;
                }
            }

            _output.WriteLine($"  Summary: Tested {testedCount} resource types, skipped {skippedCount} complex types");
        }
    }

    #endregion

    #region Scenario Validation Tests

    [Fact]
    public void GivenAllScenarios_WhenBuiltAndValidatedAcrossAllVersions_ThenAllPassValidation()
    {
        foreach (var version in _schemaProviders.Keys)
        {
            _output.WriteLine($"===== Testing Scenarios for {version} =====");
            var schemaProvider = _schemaProviders[version];

            var scenarios = GetAllTestScenarios(schemaProvider);

            foreach (var (name, scenario) in scenarios)
            {
                _output.WriteLine($"  Testing scenario: {name}");

                try
                {
                    var built = scenario.Build();

                    // Validate Patient
                    if (built.Patient != null)
                    {
                        var result = ValidateResource(built.Patient, version);
                        if (result != null)
                        {
                            var errors = GetErrorIssues(result).ToList();
                            errors.ShouldBeEmpty($"Patient in '{name}' scenario should pass validation in {version}. Issues: {string.Join(", ", errors.Select(e => $"{e.Path}: {e.Message}"))}");
                        }
                    }

                    // Validate Encounters
                    foreach (var encounter in built.Encounters)
                    {
                        var result = ValidateResource(encounter, version);
                        if (result != null)
                        {
                            var errors = GetErrorIssues(result).ToList();
                            errors.ShouldBeEmpty($"Encounter in '{name}' scenario should pass validation in {version}. Issues: {string.Join(", ", errors.Select(e => $"{e.Path}: {e.Message}"))}");
                        }
                    }

                    // Validate Observations
                    foreach (var observation in built.Observations)
                    {
                        var result = ValidateResource(observation, version);
                        if (result != null)
                        {
                            var errors = GetErrorIssues(result).ToList();
                            errors.ShouldBeEmpty($"Observation in '{name}' scenario should pass validation in {version}. Issues: {string.Join(", ", errors.Select(e => $"{e.Path}: {e.Message}"))}");
                        }
                    }

                    // Validate Conditions
                    foreach (var condition in built.Conditions)
                    {
                        var result = ValidateResource(condition, version);
                        if (result != null)
                        {
                            var errors = GetErrorIssues(result).ToList();
                            errors.ShouldBeEmpty($"Condition in '{name}' scenario should pass validation in {version}. Issues: {string.Join(", ", errors.Select(e => $"{e.Path}: {e.Message}"))}");
                        }
                    }

                    // Validate Medications
                    // Note: R5+ have significant breaking changes to MedicationRequest (medication -> CodeableReference,
                    // dosageInstruction.timing.repeat restructured, reasonCode removed, etc.)
                    // Skip R5+ MedicationRequest validation until MedicationOrderState is updated for R5+
                    if (version < FhirVersion.R5)
                    {
                        foreach (var medication in built.Medications)
                        {
                            var result = ValidateResource(medication, version);
                            if (result != null)
                            {
                                var errors = GetErrorIssues(result).ToList();
                                errors.ShouldBeEmpty($"MedicationRequest in '{name}' scenario should pass validation in {version}. Issues: {string.Join(", ", errors.Select(e => $"{e.Path}: {e.Message}"))}");
                            }
                        }
                    }

                    // Validate Procedures
                    foreach (var procedure in built.Procedures)
                    {
                        var result = ValidateResource(procedure, version);
                        if (result != null)
                        {
                            var errors = GetErrorIssues(result).ToList();
                            errors.ShouldBeEmpty($"Procedure in '{name}' scenario should pass validation in {version}. Issues: {string.Join(", ", errors.Select(e => $"{e.Path}: {e.Message}"))}");
                        }
                    }

                    // Validate DiagnosticReports
                    foreach (var report in built.DiagnosticReports)
                    {
                        var result = ValidateResource(report, version);
                        if (result != null)
                        {
                            var errors = GetErrorIssues(result).ToList();
                            errors.ShouldBeEmpty($"DiagnosticReport in '{name}' scenario should pass validation in {version}. Issues: {string.Join(", ", errors.Select(e => $"{e.Path}: {e.Message}"))}");
                        }
                    }

                    // Validate Immunizations
                    // Note: R5+ have significant breaking changes to Immunization (manufacturer -> CodeableReference,
                    // doseNumber/seriesDoses changed from positiveInt to string, etc.)
                    // Skip R5+ Immunization validation until ImmunizationState is updated for R5+
                    if (version < FhirVersion.R5)
                    {
                        foreach (var immunization in built.Immunizations)
                        {
                            var result = ValidateResource(immunization, version);
                            if (result != null)
                            {
                                var errors = GetErrorIssues(result).ToList();
                                errors.ShouldBeEmpty($"Immunization in '{name}' scenario should pass validation in {version}. Issues: {string.Join(", ", errors.Select(e => $"{e.Path}: {e.Message}"))}");
                            }
                        }
                    }

                    // Validate Allergies
                    // Note: R5+ have significant breaking changes to AllergyIntolerance
                    // (reaction.manifestation -> CodeableReference, recorder removed, etc.)
                    // Skip R5+ AllergyIntolerance validation until AllergyIntoleranceState is updated for R5+
                    if (version < FhirVersion.R5)
                    {
                        foreach (var allergy in built.Allergies)
                        {
                            var result = ValidateResource(allergy, version);
                            if (result != null)
                            {
                                var errors = GetErrorIssues(result).ToList();
                                errors.ShouldBeEmpty($"AllergyIntolerance in '{name}' scenario should pass validation in {version}. Issues: {string.Join(", ", errors.Select(e => $"{e.Path}: {e.Message}"))}");
                            }
                        }
                    }

                    // Validate ServiceRequests (R4+ only)
                    if (version != FhirVersion.Stu3)
                    {
                        foreach (var serviceRequest in built.ServiceRequests)
                        {
                            var result = ValidateResource(serviceRequest, version);
                            if (result != null)
                            {
                                var errors = GetErrorIssues(result).ToList();
                                errors.ShouldBeEmpty($"ServiceRequest in '{name}' scenario should pass validation in {version}. Issues: {string.Join(", ", errors.Select(e => $"{e.Path}: {e.Message}"))}");
                            }
                        }
                    }

                    _output.WriteLine($"    OK: '{name}' scenario passed validation in {version}");
                }
                catch (ArgumentException) when (version == FhirVersion.Stu3 && name.Contains("ServiceRequest", StringComparison.Ordinal))
                {
                        // ServiceRequest doesn't exist in STU3, skip
                    _output.WriteLine($"    SKIPPED: '{name}' scenario (ServiceRequest not available in STU3)");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"    FAILED: '{name}' scenario threw exception in {version}: {ex.Message}");
                    throw;
                }
            }
        }
    }

    [Fact]
    public void GivenAllScenarios_WhenBuilt_ThenGenerateExpectedResourceTypes()
    {
        var schemaProvider = _schemaProviders[FhirVersion.R4]; // Use R4 for this test

        var scenarios = GetAllTestScenarios(schemaProvider);

        foreach (var (name, scenario) in scenarios)
        {
            _output.WriteLine($"Testing resource types for: {name}");

            var built = scenario.Build();

            // All scenarios should have a patient
            built.Patient.ShouldNotBeNull($"'{name}' scenario should have a patient");

            // Verify expected resources based on scenario name
            if (name.Contains("Encounter", StringComparison.Ordinal))
            {
                built.Encounters.ShouldNotBeEmpty($"'{name}' scenario should have encounters");
            }

            if (name.Contains("Observation", StringComparison.Ordinal))
            {
                built.Observations.ShouldNotBeEmpty($"'{name}' scenario should have observations");
            }

            if (name.Contains("Condition", StringComparison.Ordinal))
            {
                built.Conditions.ShouldNotBeEmpty($"'{name}' scenario should have conditions");
            }

            if (name.Contains("Medication", StringComparison.Ordinal))
            {
                built.Medications.ShouldNotBeEmpty($"'{name}' scenario should have medications");
            }

            if (name.Contains("Procedure", StringComparison.Ordinal))
            {
                built.Procedures.ShouldNotBeEmpty($"'{name}' scenario should have procedures");
            }

            if (name.Contains("DiagnosticReport", StringComparison.Ordinal))
            {
                built.DiagnosticReports.ShouldNotBeEmpty($"'{name}' scenario should have diagnostic reports");
            }

            if (name.Contains("Immunization", StringComparison.Ordinal))
            {
                built.Immunizations.ShouldNotBeEmpty($"'{name}' scenario should have immunizations");
            }

            if (name.Contains("Allergy", StringComparison.Ordinal))
            {
                built.Allergies.ShouldNotBeEmpty($"'{name}' scenario should have allergies");
            }

            if (name.Contains("ServiceRequest", StringComparison.Ordinal) && !name.Contains("STU3", StringComparison.Ordinal))
            {
                built.ServiceRequests.ShouldNotBeEmpty($"'{name}' scenario should have service requests");
            }

            _output.WriteLine($"  OK: '{name}' generated expected resource types");
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Returns a collection of test scenarios covering various clinical situations.
    /// </summary>
    private Dictionary<string, ScenarioBuilder> GetAllTestScenarios(IFhirSchemaProvider schemaProvider)
    {
        var scenarios = new Dictionary<string, ScenarioBuilder>();

        // Simple Patient
        scenarios["Simple Patient"] = new ScenarioBuilder(schemaProvider)
            .WithPatient(age: 35, gender: "female");

        // Patient with Encounter
        scenarios["Patient with Encounter"] = new ScenarioBuilder(schemaProvider)
            .WithPatient(age: 42, gender: "male")
            .AddEncounter("Annual physical");

        // Patient with Observation
        scenarios["Patient with Observation"] = new ScenarioBuilder(schemaProvider)
            .WithPatient(age: 28, gender: "female")
            .AddEncounter("Lab visit")
            .AddObservation(ObservationState.BloodPressure());

        // Patient with Condition
        scenarios["Patient with Condition"] = new ScenarioBuilder(schemaProvider)
            .WithPatient(age: 55, gender: "male")
            .AddEncounter("Initial diagnosis")
            .AddState(ConditionOnsetState.Hypertension());

        // Patient with Medication
        scenarios["Patient with Medication"] = new ScenarioBuilder(schemaProvider)
            .WithPatient(age: 48, gender: "female")
            .AddEncounter("Medication management")
            .AddMedicationOrder(MedicationOrderState.Metformin500mg());

        // Patient with Procedure
        scenarios["Patient with Procedure"] = new ScenarioBuilder(schemaProvider)
            .WithPatient(age: 32, gender: "male")
            .AddEncounter("Surgery")
            .AddProcedure(ProcedureState.Appendectomy());

        // Patient with DiagnosticReport
        scenarios["Patient with DiagnosticReport"] = new ScenarioBuilder(schemaProvider)
            .WithPatient(age: 45, gender: "female")
            .AddEncounter("Lab work")
            .AddDiagnosticReport(DiagnosticReportState.ComprehensiveMetabolicPanel());

        // Patient with Immunization
        scenarios["Patient with Immunization"] = new ScenarioBuilder(schemaProvider)
            .WithPatient(age: 25, gender: "male")
            .AddEncounter("Wellness visit")
            .AddImmunization(ImmunizationState.InfluenzaAnnual());

        // Patient with AllergyIntolerance
        scenarios["Patient with Allergy"] = new ScenarioBuilder(schemaProvider)
            .WithPatient(age: 38, gender: "female")
            .AddPeanutAllergy();

        // Complex Multi-Resource Scenario
        scenarios["Complex Multi-Resource Scenario"] = new ScenarioBuilder(schemaProvider)
            .WithPatient(age: 62, gender: "male")
            .AddEncounter("Comprehensive visit")
            .AddState(ConditionOnsetState.DiabetesType2())
            .AddObservation(ObservationState.HemoglobinA1c())
            .AddObservation(ObservationState.BloodGlucose())
            .AddMedicationOrder(MedicationOrderState.Metformin1000mg())
            .AddPeanutAllergy()
            .AddDiagnosticReport(DiagnosticReportState.LipidPanel())
            .AddImmunization(ImmunizationState.InfluenzaAnnual());

        // ServiceRequest scenarios (R4+ only, will fail gracefully for STU3)
        try
        {
            scenarios["Patient with ServiceRequest"] = new ScenarioBuilder(schemaProvider)
                .WithPatient(age: 50, gender: "female")
                .AddEncounter("Lab order")
                .AddCBCOrder();

            scenarios["Patient with Multiple ServiceRequests"] = new ScenarioBuilder(schemaProvider)
                .WithPatient(age: 58, gender: "male")
                .AddEncounter("Comprehensive workup")
                .AddCBCOrder()
                .AddLipidPanelOrder()
                .AddHemoglobinA1cOrder();
        }
        catch (ArgumentException)
        {
            // ServiceRequest not available in STU3, scenarios will be skipped
        }

        return scenarios;
    }

    #endregion
}

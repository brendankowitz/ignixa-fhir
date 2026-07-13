// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.FhirFakes.Builders;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests._TestData.Fixtures.DataTypeSearch;

/// <summary>
/// Test fixture for composite search parameter tests.
/// Creates Observation and DocumentReference test data with composite search scenarios
/// (code-value-quantity, component-code-value-quantity, code-value-concept, relationship).
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.CompositeSearchTestFixture
/// </summary>
public class CompositeSearchFixture : IAsyncLifetime
{
    private readonly IgnixaApiFixture _apiFixture;

    public CompositeSearchFixture(IgnixaApiFixture apiFixture)
    {
        _apiFixture = apiFixture ?? throw new ArgumentNullException(nameof(apiFixture));
    }

    /// <summary>
    /// Unique tag for isolating test data in this fixture.
    /// </summary>
    public string Tag { get; private set; } = null!;

    /// <summary>
    /// Test patient reference for observations.
    /// </summary>
    public string PatientId { get; private set; } = null!;

    /// <summary>
    /// Observation test data for composite search testing.
    /// Index mapping (matches Microsoft FHIR Server test data):
    /// [0] = APGAR 1-minute score=10 (composite: code-value-quantity)
    /// [1] = APGAR 1-minute score=20 (composite: code-value-quantity)
    /// [2] = APGAR 20-minute score=10 (composite: code-value-quantity)
    /// [3] = Body Temperature 100 Cel
    /// [4] = TPMT diplotype *1/*1 (token-token composite)
    /// [5] = Blood Pressure systolic=107 diastolic=60 (component-code-value-quantity)
    /// [6] = Eye color blue-eyed (token-string composite)
    /// [7] = Eye color with extended length text (token-string composite)
    /// [8] = Date observation, valueDateTime=2020-06-15 (token-datetime composite, single instant)
    /// [9] = Date observation, valuePeriod=2020-01-01..2020-12-31 (token-datetime composite, straddles 2020-06-01)
    /// [10] = Date observation, valuePeriod=2020-08-01..2020-09-30 (token-datetime composite, starts after 2020-06-01)
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Observations { get; private set; } = null!;

    /// <summary>
    /// System URI for the token component of the code-value-date composite test observations.
    /// </summary>
    public const string DateCompositeCodeSystem = "http://example.org/date-composite";

    /// <summary>
    /// Observation.code used by the single-instant date observation ([8]).
    /// </summary>
    public const string DateSingleCode = "obs-date-single";

    /// <summary>
    /// Observation.code shared by the two ranged date observations ([9], [10]) used for gt-vs-sa comparison.
    /// </summary>
    public const string DateRangeCode = "obs-date-range";

    /// <summary>
    /// DocumentReference test data for reference-token composite search.
    /// Index mapping:
    /// [0] = relatesTo: replaces document1
    /// [1] = relatesTo: transforms document2
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> DocumentReferences { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Tag = Guid.NewGuid().ToString();

        var faker = _apiFixture.Harness.CreateFaker();
        var schemaProvider = faker.SchemaProvider;

        // Create a patient for all observations
        var patient = PatientBuilderFactory.Create(schemaProvider)
            .WithTag(Tag)
            .WithGivenName("Composite")
            .WithFamilyName("Search")
            .Build();

        var createdPatient = await _apiFixture.Harness.CreateResourceAsync(patient);
        PatientId = createdPatient.Id!;

        // Create observations with composite search scenarios
        Observations = await CreateObservationsAsync(schemaProvider);

        // Create document references with reference-token composites
        DocumentReferences = await CreateDocumentReferencesAsync(schemaProvider);
    }

    private async Task<IReadOnlyList<ResourceJsonNode>> CreateObservationsAsync(Ignixa.Abstractions.IFhirSchemaProvider schemaProvider)
    {
        var observations = new[]
        {
            // [0] - APGAR 1-minute score=10
            // Tests: code-value-quantity composite
            ObservationBuilder.Create(schemaProvider)
                .WithTag(Tag)
                .WithCode("9272-6", "http://loinc.org", "1 minute Apgar Score")
                .WithQuantityValue(10, "{score}", "http://unitsofmeasure.org")
                .WithSubject(PatientId)
                .WithStatus("final")
                .Build(),

            // [1] - APGAR 1-minute score=20
            ObservationBuilder.Create(schemaProvider)
                .WithTag(Tag)
                .WithCode("9272-6", "http://loinc.org", "1 minute Apgar Score")
                .WithQuantityValue(20, "{score}", "http://unitsofmeasure.org")
                .WithSubject(PatientId)
                .WithStatus("final")
                .Build(),

            // [2] - APGAR 20-minute score=10
            ObservationBuilder.Create(schemaProvider)
                .WithTag(Tag)
                .WithCode("9271-8", "http://loinc.org", "20 minute Apgar Score")
                .WithQuantityValue(10, "{score}", "http://unitsofmeasure.org")
                .WithSubject(PatientId)
                .WithStatus("final")
                .Build(),

            // [3] - Body Temperature 100 Cel
            ObservationBuilder.Create(schemaProvider)
                .WithTag(Tag)
                .WithCode("8310-5", "http://loinc.org", "Body temperature")
                .WithQuantityValue(100, "Cel", "http://unitsofmeasure.org")
                .WithSubject(PatientId)
                .WithStatus("final")
                .Build(),

            // [4] - TPMT diplotype *1/*1
            // Tests: token-token composite (code-value-concept)
            ObservationBuilder.Create(schemaProvider)
                .WithTag(Tag)
                .WithCode("79713-3", "http://loinc.org", "TPMT diplotype")
                .WithCodedValue("*1/*1", "http://pharmvar.org/haplotype")
                .WithSubject(PatientId)
                .WithStatus("final")
                .Build(),

            // [5] - Blood Pressure with component values
            // Tests: component-code-value-quantity composite
            // Built manually since ObservationBuilder doesn't support components yet
            CreateBloodPressureObservation(schemaProvider, Tag, PatientId),

            // [6] - Eye color (short text)
            // Tests: token-string composite (code-value-string)
            ObservationBuilder.Create(schemaProvider)
                .WithTag(Tag)
                .WithCode("eye-color", "http://example.org")
                .WithStringValue("blue-eyed")
                .WithSubject(PatientId)
                .WithStatus("final")
                .Build(),

            // [7] - Eye color (extended length text for partial match testing)
            ObservationBuilder.Create(schemaProvider)
                .WithTag(Tag)
                .WithCode("eye-color", "http://example.org")
                .WithStringValue("hazel eyes with a long descriptive text that exceeds normal length")
                .WithSubject(PatientId)
                .WithStatus("final")
                .Build(),

            // [8] - Date observation with a single-instant valueDateTime
            // Tests: token-datetime composite (code-value-date), basic match
            CreateDateTimeValueObservation(Tag, PatientId, DateCompositeCodeSystem, DateSingleCode, "2020-06-15"),

            // [9] - Date observation with a valuePeriod straddling 2020-06-01
            // Tests: token-datetime composite gt-vs-sa distinction (starts before, ends after)
            CreatePeriodValueObservation(Tag, PatientId, DateCompositeCodeSystem, DateRangeCode, "2020-01-01", "2020-12-31"),

            // [10] - Date observation with a valuePeriod starting after 2020-06-01
            // Tests: token-datetime composite gt-vs-sa distinction (whole range after the search value)
            CreatePeriodValueObservation(Tag, PatientId, DateCompositeCodeSystem, DateRangeCode, "2020-08-01", "2020-09-30")
        };

        return await _apiFixture.Harness.CreateResourcesAsync(observations);
    }

    private async Task<IReadOnlyList<ResourceJsonNode>> CreateDocumentReferencesAsync(Ignixa.Abstractions.IFhirSchemaProvider schemaProvider)
    {
        var documentReferences = new[]
        {
            // [0] - DocumentReference with relatesTo: replaces
            CreateDocumentReference(schemaProvider, Tag, PatientId, "replaces", "DocumentReference/document1"),

            // [1] - DocumentReference with relatesTo: transforms
            CreateDocumentReference(schemaProvider, Tag, PatientId, "transforms", "DocumentReference/document2")
        };

        return await _apiFixture.Harness.CreateResourcesAsync(documentReferences);
    }

    private static ResourceJsonNode CreateBloodPressureObservation(
        Ignixa.Abstractions.IFhirSchemaProvider schemaProvider,
        string tag,
        string patientId)
    {
        var obsJson = new JsonObject
        {
            ["resourceType"] = "Observation",
            ["id"] = Guid.NewGuid().ToString(),
            ["meta"] = new JsonObject
            {
                ["tag"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = "http://test.ignixa.io/tag",
                        ["code"] = tag
                    }
                }
            },
            ["status"] = "final",
            ["code"] = new JsonObject
            {
                ["coding"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = "http://loinc.org",
                        ["code"] = "85354-9",
                        ["display"] = "Blood pressure panel"
                    }
                }
            },
            ["subject"] = new JsonObject
            {
                ["reference"] = $"Patient/{patientId}"
            },
            ["component"] = new JsonArray
            {
                // Systolic BP component
                new JsonObject
                {
                    ["code"] = new JsonObject
                    {
                        ["coding"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["system"] = "http://loinc.org",
                                ["code"] = "8480-6",
                                ["display"] = "Systolic blood pressure"
                            }
                        }
                    },
                    ["valueQuantity"] = new JsonObject
                    {
                        ["value"] = 107,
                        ["unit"] = "mmHg",
                        ["system"] = "http://unitsofmeasure.org",
                        ["code"] = "mmHg"
                    }
                },
                // Diastolic BP component
                new JsonObject
                {
                    ["code"] = new JsonObject
                    {
                        ["coding"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["system"] = "http://loinc.org",
                                ["code"] = "8462-4",
                                ["display"] = "Diastolic blood pressure"
                            }
                        }
                    },
                    ["valueQuantity"] = new JsonObject
                    {
                        ["value"] = 60,
                        ["unit"] = "mmHg",
                        ["system"] = "http://unitsofmeasure.org",
                        ["code"] = "mmHg"
                    }
                }
            }
        };

        var json = obsJson.ToJsonString();
        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
    }

    private static ResourceJsonNode CreateDateTimeValueObservation(
        string tag,
        string patientId,
        string codeSystem,
        string code,
        string valueDateTime)
    {
        var obsJson = CreateDateObservationBase(tag, patientId, codeSystem, code);
        obsJson["valueDateTime"] = valueDateTime;
        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(obsJson.ToJsonString());
    }

    private static ResourceJsonNode CreatePeriodValueObservation(
        string tag,
        string patientId,
        string codeSystem,
        string code,
        string periodStart,
        string periodEnd)
    {
        var obsJson = CreateDateObservationBase(tag, patientId, codeSystem, code);
        obsJson["valuePeriod"] = new JsonObject
        {
            ["start"] = periodStart,
            ["end"] = periodEnd
        };
        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(obsJson.ToJsonString());
    }

    private static JsonObject CreateDateObservationBase(
        string tag,
        string patientId,
        string codeSystem,
        string code)
    {
        return new JsonObject
        {
            ["resourceType"] = "Observation",
            ["id"] = Guid.NewGuid().ToString(),
            ["meta"] = new JsonObject
            {
                ["tag"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = "http://test.ignixa.io/tag",
                        ["code"] = tag
                    }
                }
            },
            ["status"] = "final",
            ["code"] = new JsonObject
            {
                ["coding"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = codeSystem,
                        ["code"] = code
                    }
                }
            },
            ["subject"] = new JsonObject
            {
                ["reference"] = $"Patient/{patientId}"
            }
        };
    }

    private static ResourceJsonNode CreateDocumentReference(
        Ignixa.Abstractions.IFhirSchemaProvider schemaProvider,
        string tag,
        string patientId,
        string relatesToCode,
        string targetReference)
    {
        var docRefJson = new JsonObject
        {
            ["resourceType"] = "DocumentReference",
            ["id"] = Guid.NewGuid().ToString(),
            ["meta"] = new JsonObject
            {
                ["tag"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = "http://test.ignixa.io/tag",
                        ["code"] = tag
                    }
                }
            },
            ["status"] = "current",
            ["subject"] = new JsonObject
            {
                ["reference"] = $"Patient/{patientId}"
            },
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["attachment"] = new JsonObject
                    {
                        ["contentType"] = "text/plain"
                    }
                }
            },
            ["relatesTo"] = new JsonArray
            {
                new JsonObject
                {
                    ["code"] = relatesToCode,
                    ["target"] = new JsonObject
                    {
                        ["reference"] = targetReference
                    }
                }
            }
        };

        var json = docRefJson.ToJsonString();
        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
    }

    public Task DisposeAsync()
    {
        // Cleanup handled by tag isolation - no explicit cleanup needed
        return Task.CompletedTask;
    }
}

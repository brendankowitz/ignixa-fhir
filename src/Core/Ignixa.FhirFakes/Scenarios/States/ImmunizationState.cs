// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Bogus;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.Specification;
using FhirCode = Ignixa.FhirFakes.Scenarios.Codes.FhirCode;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// State that creates an Immunization resource.
/// Immunizations represent vaccines administered with dose tracking and series information.
/// </summary>
public sealed class ImmunizationState : ScenarioState
{
    private readonly Faker _faker = new();

    /// <summary>
    /// Gets or sets the vaccine code (from Immunizations constants).
    /// </summary>
    public required FhirCode Code { get; init; }

    /// <summary>
    /// Gets or sets the immunization status ("completed", "entered-in-error", "not-done").
    /// </summary>
    public string Status { get; init; } = "completed";

    /// <summary>
    /// Gets or sets the dose number in the series.
    /// </summary>
    public int DoseNumber { get; init; } = 1;

    /// <summary>
    /// Gets or sets the vaccine series name (e.g., "Childhood Immunization Series").
    /// </summary>
    public string? Series { get; init; }

    /// <summary>
    /// Gets or sets the number of doses required in the series.
    /// </summary>
    public int? SeriesDosesRecommended { get; init; }

    /// <summary>
    /// Gets or sets the route of administration ("IM", "oral", "intranasal", "SC", "ID").
    /// </summary>
    public string Route { get; init; } = "IM";

    /// <summary>
    /// Gets or sets the administration site ("left arm", "right arm", "left thigh", "right thigh").
    /// </summary>
    public string? Site { get; init; }

    /// <summary>
    /// Gets or sets the dose quantity value (default is 0.5 mL for most vaccines).
    /// </summary>
    public decimal DoseQuantity { get; init; } = 0.5m;

    /// <summary>
    /// Gets or sets the dose unit (default is "mL").
    /// </summary>
    public string DoseUnit { get; init; } = "mL";

    /// <summary>
    /// Gets or sets the vaccine manufacturer name.
    /// </summary>
    public string? Manufacturer { get; init; }

    /// <summary>
    /// Gets or sets the vaccine lot number.
    /// </summary>
    public string? LotNumber { get; init; }

    /// <summary>
    /// Gets or sets the vaccine expiration date.
    /// </summary>
    public DateTime? ExpirationDate { get; init; }

    /// <summary>
    /// Creates an Immunization resource linked to the patient and optionally to an encounter.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);

        if (context.Patient is null)
        {
            throw new InvalidOperationException("Cannot create Immunization without a Patient. Ensure InitialState runs first.");
        }

        var immunization = faker.Generate("Immunization");
        var node = immunization.MutableNode;

        // Set required fields
        node["id"] = Guid.NewGuid().ToString();
        node["status"] = Status;

        // Set vaccine code
        node["vaccineCode"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = Code.System,
                    ["code"] = Code.Code,
                    ["display"] = Code.Display
                }
            },
            ["text"] = Code.Display
        };

        // Set patient reference
        node["patient"] = new JsonObject
        {
            ["reference"] = $"Patient/{context.Patient.Id}"
        };

        // Set encounter reference if available
        if (context.CurrentEncounter is not null)
        {
            node["encounter"] = new JsonObject
            {
                ["reference"] = $"Encounter/{context.CurrentEncounter.Id}"
            };
        }
        else
        {
            // Clear any faker-generated encounter reference
            node.Remove("encounter");
        }

        // Remove any existing choice element variants to avoid conflicts
        // The faker may generate placeholder values for choice elements
        RemoveChoiceConflicts(node, "occurrence");

        // Set occurrence date using version-appropriate field name (R4+ normative is "occurrenceDateTime", STU3 is "date")
        var occurrenceField = VersionFieldOverrides.GetFieldName(
            faker.SchemaProvider.Version,
            "Immunization",
            "occurrenceDateTime");
        node[occurrenceField] = context.CurrentTime.ToString("o");

        // Set primary source (true = data was recorded by administering provider)
        node["primarySource"] = true;

        // Set manufacturer
        var manufacturerName = Manufacturer ?? GenerateManufacturer();
        node["manufacturer"] = new JsonObject
        {
            ["display"] = manufacturerName
        };

        // Set lot number
        var lotNum = LotNumber ?? GenerateLotNumber();
        node["lotNumber"] = lotNum;

        // Set expiration date
        var expDate = ExpirationDate ?? context.CurrentTime.AddYears(2);
        node["expirationDate"] = expDate.ToString("yyyy-MM-dd");

        // Set administration site
        var administrationSite = Site ?? GenerateSite();
        node["site"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "http://terminology.hl7.org/CodeSystem/v3-ActSite",
                    ["code"] = MapSiteToCode(administrationSite),
                    ["display"] = administrationSite
                }
            }
        };

        // Set route of administration
        node["route"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "http://terminology.hl7.org/CodeSystem/v3-RouteOfAdministration",
                    ["code"] = MapRouteToCode(Route),
                    ["display"] = MapRouteToDisplay(Route)
                }
            }
        };

        // Set dose quantity
        node["doseQuantity"] = new JsonObject
        {
            ["value"] = DoseQuantity,
            ["unit"] = DoseUnit,
            ["system"] = FhirCode.Systems.Ucum,
            ["code"] = DoseUnit
        };

        // Set performer (administering provider)
        // In STU3, use "practitioner" (BackboneElement[] with role and actor).
        // In R4+, use "performer" (BackboneElement[] with function and actor).
        var isSTU3 = faker.SchemaProvider.Version == Ignixa.Abstractions.FhirVersion.Stu3;

        var performerRef = new JsonObject
        {
            ["display"] = _faker.Name.FullName() + ", RN"
        };

        // Add practitioner reference if available
        if (context.CurrentPractitioner is not null)
        {
            performerRef["reference"] = $"Practitioner/{context.CurrentPractitioner.Id}";
        }

        if (isSTU3)
        {
            // STU3: BackboneElement[] with role (CodeableConcept) and actor (Reference)
            node["practitioner"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = new JsonObject
                    {
                        ["coding"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["system"] = "http://hl7.org/fhir/v2/0443",
                                ["code"] = "AP",
                                ["display"] = "Administering Provider"
                            }
                        }
                    },
                    ["actor"] = performerRef
                }
            };
        }
        else
        {
            // R4+: BackboneElement[] with function and actor
            node["performer"] = new JsonArray
            {
                new JsonObject
                {
                    ["function"] = new JsonObject
                    {
                        ["coding"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["system"] = "http://terminology.hl7.org/CodeSystem/v2-0443",
                                ["code"] = "AP",
                                ["display"] = "Administering Provider"
                            }
                        }
                    },
                    ["actor"] = performerRef
                }
            };
        }

        // Set protocol applied (dose series tracking) - version-aware field naming
        var protocolFieldName = faker.SchemaProvider.GetImmunizationProtocolFieldName();
        node[protocolFieldName] = new JsonArray
        {
            CreateProtocolApplied(faker.SchemaProvider)
        };

        // Add to context
        context.AddImmunization(immunization, Code.Display);
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private JsonObject CreateProtocolApplied(IFhirSchemaProvider schemaProvider)
    {
        var protocol = new JsonObject();
        var isSTU3 = schemaProvider.Version == Ignixa.Abstractions.FhirVersion.Stu3;

        // Version-aware dose number field
        var doseNumberFieldName = schemaProvider.GetImmunizationDoseNumberFieldName();
        protocol[doseNumberFieldName] = DoseNumber;

        // Series field is common across versions
        if (!string.IsNullOrEmpty(Series))
        {
            protocol["series"] = Series;
        }

        // Version-aware series doses field (not present in STU3)
        if (SeriesDosesRecommended.HasValue)
        {
            var seriesDosesFieldName = schemaProvider.GetImmunizationSeriesDosesFieldName();
            if (seriesDosesFieldName is not null)
            {
                protocol[seriesDosesFieldName] = SeriesDosesRecommended.Value;
            }
        }

        // STU3-specific required fields: targetDisease and doseStatus
        if (isSTU3)
        {
            // targetDisease is required in STU3 - use a generic code based on vaccine
            protocol["targetDisease"] = new JsonArray
            {
                new JsonObject
                {
                    ["coding"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["system"] = FhirCode.Systems.SnomedCt,
                            ["code"] = "840539006", // Disease caused by SARS-CoV-2 (generic disease code)
                            ["display"] = "Infectious disease"
                        }
                    },
                    ["text"] = "Target disease"
                }
            };

            // doseStatus is required in STU3
            protocol["doseStatus"] = new JsonObject
            {
                ["coding"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = "http://terminology.hl7.org/CodeSystem/vaccination-protocol-dose-status",
                        ["code"] = "count",
                        ["display"] = "Counts"
                    }
                }
            };
        }

        return protocol;
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private string GenerateManufacturer()
    {
        var manufacturers = new[]
        {
            "Pfizer Inc.",
            "Moderna, Inc.",
            "Merck & Co., Inc.",
            "GlaxoSmithKline Biologicals",
            "Sanofi Pasteur Inc.",
            "Johnson & Johnson",
            "AstraZeneca"
        };
        return manufacturers[_faker.Random.Int(0, manufacturers.Length - 1)];
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private string GenerateLotNumber()
    {
        var letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var letter1 = letters[_faker.Random.Int(0, 25)];
        var letter2 = letters[_faker.Random.Int(0, 25)];
        return $"{letter1}{letter2}{_faker.Random.Int(10000, 99999)}";
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private string GenerateSite()
    {
        var sites = new[] { "left arm", "right arm", "left thigh", "right thigh" };
        return sites[_faker.Random.Int(0, sites.Length - 1)];
    }

    private static string MapSiteToCode(string site) => site.ToUpperInvariant() switch
    {
        "LEFT ARM" => "LA",
        "RIGHT ARM" => "RA",
        "LEFT THIGH" => "LT",
        "RIGHT THIGH" => "RT",
        _ => "LA"
    };

    private static string MapRouteToCode(string route) => route.ToUpperInvariant() switch
    {
        "IM" => "IM",
        "SC" or "SUBCUTANEOUS" => "SC",
        "ID" or "INTRADERMAL" => "IDINJ",
        "ORAL" => "PO",
        "INTRANASAL" => "NASINHLC",
        _ => "IM"
    };

    private static string MapRouteToDisplay(string route) => route.ToUpperInvariant() switch
    {
        "IM" => "Intramuscular",
        "SC" or "SUBCUTANEOUS" => "Subcutaneous",
        "ID" or "INTRADERMAL" => "Intradermal",
        "ORAL" => "Oral",
        "INTRANASAL" => "Intranasal",
        _ => "Intramuscular"
    };

    #region Factory Methods

    /// <summary>
    /// Creates an MMR (Measles, Mumps, Rubella) vaccination - dose 1.
    /// </summary>
    public static ImmunizationState MMRDose1() => new()
    {
        Code = Immunizations.MMR,
        DoseNumber = 1,
        Series = "Childhood Immunization Series",
        SeriesDosesRecommended = 2,
        Site = "left arm"
    };

    /// <summary>
    /// Creates an MMR (Measles, Mumps, Rubella) vaccination - dose 2.
    /// </summary>
    public static ImmunizationState MMRDose2() => new()
    {
        Code = Immunizations.MMR,
        DoseNumber = 2,
        Series = "Childhood Immunization Series",
        SeriesDosesRecommended = 2,
        Site = "right arm"
    };

    /// <summary>
    /// Creates an annual influenza vaccination.
    /// </summary>
    public static ImmunizationState InfluenzaAnnual() => new()
    {
        Code = Immunizations.Influenza,
        DoseNumber = 1,
        Series = "Annual Influenza",
        SeriesDosesRecommended = 1
    };

    /// <summary>
    /// Creates a DTaP (Diphtheria, Tetanus, Pertussis) vaccination.
    /// </summary>
    public static ImmunizationState DTaP(int doseNumber = 1) => new()
    {
        Code = Immunizations.DTaP,
        DoseNumber = doseNumber,
        Series = "Childhood Immunization Series",
        SeriesDosesRecommended = 5
    };

    /// <summary>
    /// Creates a Hepatitis B vaccination.
    /// </summary>
    public static ImmunizationState HepB(int doseNumber = 1) => new()
    {
        Code = Immunizations.HepB,
        DoseNumber = doseNumber,
        Series = "Hepatitis B Series",
        SeriesDosesRecommended = 3
    };

    /// <summary>
    /// Creates a COVID-19 Pfizer vaccination.
    /// </summary>
    public static ImmunizationState Covid19Pfizer(int doseNumber = 1) => new()
    {
        Code = Immunizations.Covid19Pfizer,
        DoseNumber = doseNumber,
        Series = "COVID-19 Primary Series",
        SeriesDosesRecommended = 2,
        DoseQuantity = 0.3m,
        Manufacturer = "Pfizer Inc."
    };

    /// <summary>
    /// Creates a COVID-19 Moderna vaccination.
    /// </summary>
    public static ImmunizationState Covid19Moderna(int doseNumber = 1) => new()
    {
        Code = Immunizations.Covid19Moderna,
        DoseNumber = doseNumber,
        Series = "COVID-19 Primary Series",
        SeriesDosesRecommended = 2,
        Manufacturer = "Moderna, Inc."
    };

    /// <summary>
    /// Creates a Tdap (Tetanus, Diphtheria, Pertussis) booster vaccination.
    /// </summary>
    public static ImmunizationState TdapBooster() => new()
    {
        Code = Immunizations.Tdap,
        DoseNumber = 1,
        Series = "Adolescent/Adult Booster",
        SeriesDosesRecommended = 1
    };

    /// <summary>
    /// Creates an HPV (Human Papillomavirus) vaccination.
    /// </summary>
    public static ImmunizationState HPV(int doseNumber = 1) => new()
    {
        Code = Immunizations.HPV,
        DoseNumber = doseNumber,
        Series = "HPV Series",
        SeriesDosesRecommended = 3
    };

    /// <summary>
    /// Creates a Pneumococcal vaccine (PCV13).
    /// </summary>
    public static ImmunizationState PneumococcalPCV13(int doseNumber = 1) => new()
    {
        Code = Immunizations.PCV13,
        DoseNumber = doseNumber,
        Series = "Pneumococcal Series",
        SeriesDosesRecommended = 4
    };

    /// <summary>
    /// Creates a Varicella (Chickenpox) vaccination.
    /// </summary>
    public static ImmunizationState Varicella(int doseNumber = 1) => new()
    {
        Code = Immunizations.Varicella,
        DoseNumber = doseNumber,
        Series = "Varicella Series",
        SeriesDosesRecommended = 2
    };

    #endregion
}

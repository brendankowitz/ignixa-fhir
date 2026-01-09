// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Bogus;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.Scenarios.Codes;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// State that creates a Practitioner resource.
/// Practitioners are healthcare providers who can be referenced in Encounters, MedicationRequests, and Procedures.
/// </summary>
/// <remarks>
/// This state generates FHIR Practitioner resources with:
/// <list type="bullet">
///   <item><description>NPI identifier (National Provider Identifier)</description></item>
///   <item><description>Name with optional prefix and suffix</description></item>
///   <item><description>Gender</description></item>
///   <item><description>Qualification codes (board certifications)</description></item>
///   <item><description>Specialty via PractitionerRole reference</description></item>
/// </list>
/// </remarks>
public sealed class PractitionerState : ScenarioState
{
    private readonly Faker _faker = new();
    private PractitionerBuilder? _builder;

    /// <summary>
    /// Gets the specialty code for the practitioner (from Specialties constants).
    /// Uses SNOMED CT codes for medical specialties.
    /// </summary>
    public required FhirCode Specialty { get; init; }

    /// <summary>
    /// Gets or sets the name prefix (e.g., "Dr.", "Prof.").
    /// If not specified, defaults to "Dr." for physicians.
    /// </summary>
    public string? Prefix { get; init; }

    /// <summary>
    /// Gets or sets the practitioner's given (first) name.
    /// If not specified, a random name will be generated using Bogus faker.
    /// </summary>
    public string? GivenName { get; init; }

    /// <summary>
    /// Gets or sets the practitioner's family (last) name.
    /// If not specified, a random name will be generated using Bogus faker.
    /// </summary>
    public string? FamilyName { get; init; }

    /// <summary>
    /// Gets or sets the name suffix (e.g., "MD", "DO", "RN", "NP").
    /// If not specified, inferred from the specialty.
    /// </summary>
    public string? Suffix { get; init; }

    /// <summary>
    /// Gets or sets the practitioner's administrative gender ("male", "female", "other", "unknown").
    /// If not specified, a random gender will be generated.
    /// </summary>
    public string? Gender { get; init; }

    /// <summary>
    /// Gets or sets the list of qualification codes (board certifications).
    /// Examples: "Board Certified Internal Medicine", "ABIM", etc.
    /// </summary>
    public IReadOnlyList<string>? Qualifications { get; init; }

    /// <summary>
    /// Gets or sets the National Provider Identifier (NPI) number.
    /// If not specified, a valid NPI will be auto-generated with Luhn check digit.
    /// </summary>
    public string? NpiNumber { get; init; }

    /// <summary>
    /// Gets or sets whether this is an organizational practitioner (Type 2 NPI).
    /// If false (default), generates a Type 1 NPI for individual practitioners.
    /// </summary>
    public bool IsOrganization { get; init; }

    /// <summary>
    /// Creates a Practitioner resource and stores it in the context.
    /// </summary>
    /// <param name="context">The scenario context containing patient state and resources.</param>
    /// <param name="faker">The resource faker for generating realistic FHIR resources.</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    [SuppressMessage("Globalization", "CA1308:Replace ToLower with ToUpperInvariant", Justification = "Email addresses are conventionally lowercase")]
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);

        // Initialize builder
        _builder = PractitionerBuilder.Create(faker.SchemaProvider);

        // Generate name components
        var givenName = GivenName ?? _faker.Name.FirstName();
        var familyName = FamilyName ?? _faker.Name.LastName();
        var prefix = Prefix ?? InferPrefix();
        var suffix = Suffix ?? InferSuffix();

        // Delegate basic building to PractitionerBuilder
        _builder.WithName(givenName, familyName);

        // Generate or use provided NPI
        var npi = NpiNumber ?? GenerateNpi(IsOrganization);
        _builder.WithNpi(npi);

        // Add specialty as qualification
        _builder.WithSpecialty(Specialty.Code, Specialty.System, Specialty.Display);

        // Apply scenario tag if present
        if (faker.Tag is not null)
        {
            _builder.WithTag(faker.Tag);
        }

        // Build the base resource from builder
        var practitioner = _builder.Build();
        var node = practitioner.MutableNode;

        // Add scenario-specific fields not supported by builder
        // Set identifier use to "official"
        if (node["identifier"] is JsonArray identifiers && identifiers.Count > 0)
        {
            if (identifiers[0] is JsonObject firstIdentifier)
            {
                firstIdentifier["use"] = "official";
            }
        }

        // Add prefix/suffix to name
        if (node["name"] is JsonArray names && names.Count > 0)
        {
            if (names[0] is JsonObject nameObject)
            {
                if (!string.IsNullOrEmpty(prefix))
                {
                    nameObject["prefix"] = new JsonArray { prefix };
                }

                if (!string.IsNullOrEmpty(suffix))
                {
                    nameObject["suffix"] = new JsonArray { suffix };
                }
            }
        }

        // Set gender
        var gender = Gender ?? _faker.PickRandom(PatientBuilderConstants.Gender.BinaryOnly);
        node["gender"] = gender;

        // Set telecom (phone and email)
        node["telecom"] = new JsonArray
        {
            new JsonObject
            {
                ["system"] = "phone",
                ["value"] = _faker.Phone.PhoneNumber("###-###-####"),
                ["use"] = "work"
            },
            new JsonObject
            {
                ["system"] = "email",
                ["value"] = $"{givenName.ToLower(System.Globalization.CultureInfo.InvariantCulture)}.{familyName.ToLower(System.Globalization.CultureInfo.InvariantCulture)}@hospital.example.org",
                ["use"] = "work"
            }
        };

        // Set address
        node["address"] = new JsonArray
        {
            new JsonObject
            {
                ["use"] = "work",
                ["type"] = "physical",
                ["line"] = new JsonArray { _faker.Address.StreetAddress() },
                ["city"] = _faker.Address.City(),
                ["state"] = _faker.Address.StateAbbr(),
                ["postalCode"] = _faker.Address.ZipCode(),
                ["country"] = "US"
            }
        };

        // Enhance qualifications with scenario-specific details
        EnhanceQualifications(node);

        // Add to context
        context.AddPractitioner(practitioner, Specialty.Display);
        context.SetCurrentPractitioner(practitioner);

        // NEW: Register with StateId for cross-references
        context.RegisterStateResource(StateId, practitioner);
    }

    /// <summary>
    /// Generates a valid 10-digit National Provider Identifier (NPI) with Luhn check digit.
    /// </summary>
    /// <param name="isOrganization">If true, generates Type 2 (organizational) NPI with prefix "2"; otherwise Type 1 (individual) with prefix "1".</param>
    /// <returns>A valid 10-digit NPI string.</returns>
    /// <remarks>
    /// NPI Format:
    /// - First digit: 1 for individual practitioners (Type 1), 2 for organizations (Type 2)
    /// - Digits 2-9: Random digits
    /// - Digit 10: Luhn check digit calculated using the Luhn algorithm
    ///
    /// The Luhn algorithm for NPI uses a constant prefix of "80840" before the 9-digit NPI
    /// (excluding check digit) to calculate the check digit.
    /// </remarks>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public static string GenerateNpi(bool isOrganization = false)
    {
        var random = new Random();

        // First digit: 1 for individual (Type 1), 2 for organization (Type 2)
        var prefix = isOrganization ? "2" : "1";

        // Generate 8 random digits (positions 2-9)
        var randomDigits = new char[8];
        for (int i = 0; i < 8; i++)
        {
            randomDigits[i] = (char)('0' + random.Next(10));
        }

        // Combine prefix and random digits to form first 9 digits
        var nineDigits = prefix + new string(randomDigits);

        // Calculate Luhn check digit
        var checkDigit = CalculateLuhnCheckDigit(nineDigits);

        return nineDigits + checkDigit;
    }

    /// <summary>
    /// Validates whether an NPI has a valid Luhn check digit.
    /// </summary>
    /// <param name="npi">The 10-digit NPI to validate.</param>
    /// <returns>True if the NPI has a valid Luhn check digit; otherwise false.</returns>
    public static bool ValidateNpi(string npi)
    {
        if (string.IsNullOrEmpty(npi) || npi.Length != 10)
        {
            return false;
        }

        if (!npi.All(char.IsDigit))
        {
            return false;
        }

        var nineDigits = npi[..9];
        var expectedCheckDigit = CalculateLuhnCheckDigit(nineDigits);

        return npi[9] == expectedCheckDigit;
    }

    /// <summary>
    /// Calculates the Luhn check digit for NPI validation.
    /// </summary>
    /// <param name="nineDigits">The first 9 digits of the NPI.</param>
    /// <returns>The calculated check digit character.</returns>
    /// <remarks>
    /// The NPI check digit is calculated using a modified Luhn algorithm:
    /// 1. Prefix the 9-digit NPI with "80840" (CMS constant)
    /// 2. Double every other digit starting from the rightmost position
    /// 3. Sum all digits (if doubled digit > 9, sum the individual digits)
    /// 4. Check digit = (10 - (sum mod 10)) mod 10
    /// </remarks>
    private static char CalculateLuhnCheckDigit(string nineDigits)
    {
        // Prepend the CMS constant "80840" to the 9 digits
        var fullNumber = "80840" + nineDigits;

        var sum = 0;
        var alternate = true; // Start with doubling (from right, the check digit position)

        // Process from right to left (but we're calculating for position 10, so start doubling)
        for (int i = fullNumber.Length - 1; i >= 0; i--)
        {
            var digit = fullNumber[i] - '0';

            if (alternate)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9; // Same as summing digits: 12 -> 1+2=3 -> 12-9=3
                }
            }

            sum += digit;
            alternate = !alternate;
        }

        // Calculate check digit
        var checkDigit = (10 - (sum % 10)) % 10;
        return (char)('0' + checkDigit);
    }

    /// <summary>
    /// Infers an appropriate name prefix based on specialty.
    /// </summary>
    private string InferPrefix()
    {
        // Nurses typically don't use "Dr." prefix
        if (Specialty.Code is "224535009" or "224571005")
        {
            return string.Empty;
        }

        // Physician assistants don't use "Dr." prefix
        if (Specialty.Code == "449161006")
        {
            return string.Empty;
        }

        return "Dr.";
    }

    /// <summary>
    /// Infers an appropriate name suffix based on specialty.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private string InferSuffix()
    {
        // Registered nurse
        if (Specialty.Code == "224535009")
        {
            return "RN";
        }

        // Nurse practitioner
        if (Specialty.Code == "224571005")
        {
            return "NP";
        }

        // Physician assistant
        if (Specialty.Code == "449161006")
        {
            return "PA-C";
        }

        // Default to MD for physicians, with occasional DO
        return _faker.Random.Bool(0.9f) ? "MD" : "DO";
    }

    /// <summary>
    /// Enhances qualifications created by PractitionerBuilder with scenario-specific details.
    /// Adds identifier, period, issuer, and text to the specialty qualification.
    /// Adds additional qualifications if provided.
    /// </summary>
    /// <param name="node">The Practitioner resource JSON node.</param>
    private void EnhanceQualifications(JsonObject node)
    {
        if (node["qualification"] is not JsonArray qualifications)
        {
            return;
        }

        // Enhance the first qualification (specialty) with additional details
        if (qualifications.Count > 0 && qualifications[0] is JsonObject specialtyQual)
        {
            // Add identifier
            specialtyQual["identifier"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "http://example.org/qualifications",
                    ["value"] = $"QUAL-{Guid.NewGuid():N}"[..16]
                }
            };

            // Add text to code
            if (specialtyQual["code"] is JsonObject code)
            {
                code["text"] = $"Board Certified - {Specialty.Display}";
            }

            // Add period
            specialtyQual["period"] = new JsonObject
            {
                ["start"] = DateTime.UtcNow.AddYears(-10).ToString("yyyy-MM-dd")
            };

            // Add issuer
            specialtyQual["issuer"] = new JsonObject
            {
                ["display"] = "American Board of Medical Specialties"
            };
        }

        // Add any additional qualifications provided
        if (Qualifications is { Count: > 0 })
        {
            foreach (var qual in Qualifications)
            {
                qualifications.Add(new JsonObject
                {
                    ["code"] = new JsonObject
                    {
                        ["text"] = qual
                    }
                });
            }
        }
    }

    #region Factory Methods

    /// <summary>
    /// Creates a Family Medicine practitioner (primary care physician).
    /// </summary>
    /// <returns>A configured PractitionerState for a family medicine physician.</returns>
    public static PractitionerState FamilyPractitioner() => new()
    {
        Specialty = Specialties.FamilyMedicine,
        Qualifications = ["ABFM Board Certified"]
    };

    /// <summary>
    /// Creates a Pediatrician (children's doctor).
    /// </summary>
    /// <returns>A configured PractitionerState for a pediatrician.</returns>
    public static PractitionerState Pediatrician() => new()
    {
        Specialty = Specialties.Pediatrics,
        Qualifications = ["ABP Board Certified"]
    };

    /// <summary>
    /// Creates a Cardiologist (heart specialist).
    /// </summary>
    /// <returns>A configured PractitionerState for a cardiologist.</returns>
    public static PractitionerState Cardiologist() => new()
    {
        Specialty = Specialties.Cardiology,
        Qualifications = ["ABIM Board Certified - Cardiovascular Disease"]
    };

    /// <summary>
    /// Creates an Emergency Medicine physician.
    /// </summary>
    /// <returns>A configured PractitionerState for an emergency medicine physician.</returns>
    public static PractitionerState EmergencyPhysician() => new()
    {
        Specialty = Specialties.EmergencyMedicine,
        Qualifications = ["ABEM Board Certified"]
    };

    /// <summary>
    /// Creates a General Surgeon.
    /// </summary>
    /// <returns>A configured PractitionerState for a general surgeon.</returns>
    public static PractitionerState Surgeon() => new()
    {
        Specialty = Specialties.GeneralSurgery,
        Qualifications = ["ABS Board Certified"]
    };

    /// <summary>
    /// Creates a Registered Nurse.
    /// </summary>
    /// <returns>A configured PractitionerState for a registered nurse.</returns>
    public static PractitionerState Nurse() => new()
    {
        Specialty = Specialties.Nursing,
        Qualifications = ["RN License"]
    };

    /// <summary>
    /// Creates a Nurse Practitioner.
    /// </summary>
    /// <returns>A configured PractitionerState for a nurse practitioner.</returns>
    public static PractitionerState NursePractitioner() => new()
    {
        Specialty = Specialties.NursePractitioner,
        Qualifications = ["AANP Certified", "RN License"]
    };

    /// <summary>
    /// Creates an Internal Medicine physician.
    /// </summary>
    /// <returns>A configured PractitionerState for an internal medicine physician.</returns>
    public static PractitionerState Internist() => new()
    {
        Specialty = Specialties.InternalMedicine,
        Qualifications = ["ABIM Board Certified"]
    };

    /// <summary>
    /// Creates an OB/GYN physician.
    /// </summary>
    /// <returns>A configured PractitionerState for an OB/GYN.</returns>
    public static PractitionerState ObGyn() => new()
    {
        Specialty = Specialties.ObstetricsGynecology,
        Qualifications = ["ABOG Board Certified"]
    };

    /// <summary>
    /// Creates a Psychiatrist (mental health physician).
    /// </summary>
    /// <returns>A configured PractitionerState for a psychiatrist.</returns>
    public static PractitionerState Psychiatrist() => new()
    {
        Specialty = Specialties.Psychiatry,
        Qualifications = ["ABPN Board Certified"]
    };

    /// <summary>
    /// Creates an Orthopedic Surgeon.
    /// </summary>
    /// <returns>A configured PractitionerState for an orthopedic surgeon.</returns>
    public static PractitionerState OrthopedicSurgeon() => new()
    {
        Specialty = Specialties.OrthopedicSurgery,
        Qualifications = ["ABOS Board Certified"]
    };

    /// <summary>
    /// Creates a Physician Assistant.
    /// </summary>
    /// <returns>A configured PractitionerState for a physician assistant.</returns>
    public static PractitionerState PhysicianAssistant() => new()
    {
        Specialty = Specialties.PhysicianAssistant,
        Qualifications = ["NCCPA Certified"]
    };

    #endregion
}

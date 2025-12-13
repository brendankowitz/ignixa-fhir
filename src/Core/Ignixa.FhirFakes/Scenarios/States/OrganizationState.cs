// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Bogus;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.Population;
using Ignixa.FhirFakes.Scenarios.Codes;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// State that creates an Organization resource.
/// Organizations represent healthcare facilities, payers, insurance companies, and other entities.
/// </summary>
/// <remarks>
/// This state generates realistic Organization resources including:
/// - Type 2 NPI numbers (organizations) with valid Luhn check digits
/// - Tax IDs (EIN/TIN) for billing purposes
/// - Contact information (phone, email)
/// - Realistic addresses using demographics data
/// - Organization type codes from HL7 terminology
///
/// This state internally uses OrganizationBuilder for resource construction,
/// adding scenario-specific orchestration on top (auto-generation, tagging, context integration).
/// </remarks>
public sealed class OrganizationState : ScenarioState
{
    private static readonly Faker StaticFaker = new();
    private readonly Faker _faker = new();
    private readonly DemographicsDataProvider _demographics = DemographicsDataProvider.CreateDefault();

    /// <summary>
    /// Gets the organization name.
    /// </summary>
    public required string OrganizationName { get; init; }

    /// <summary>
    /// Gets the organization type code (from http://terminology.hl7.org/CodeSystem/organization-type).
    /// Valid values: prov, dept, team, govt, ins, pay, edu, reli, crs, cg, bus, other.
    /// </summary>
    public FhirCode? Type { get; init; }

    /// <summary>
    /// Gets the NPI number (Type 2 for organizations).
    /// If not provided, a valid NPI will be auto-generated.
    /// </summary>
    public string? NpiNumber { get; init; }

    /// <summary>
    /// Gets the Tax ID (EIN/TIN) for billing.
    /// Format: XX-XXXXXXX (9 digits total).
    /// If not provided, a synthetic Tax ID will be auto-generated.
    /// </summary>
    public string? TaxId { get; init; }

    /// <summary>
    /// Gets custom identifiers to add beyond NPI and Tax ID.
    /// Each tuple contains (system, value).
    /// Example: [("http://example.org/test-ids", "unique-guid-12345")]
    /// </summary>
    public IReadOnlyList<(string System, string Value)>? CustomIdentifiers { get; init; }

    /// <summary>
    /// Gets the organization's contact phone number.
    /// </summary>
    public string? Phone { get; init; }

    /// <summary>
    /// Gets the organization's contact email address.
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// Gets the organization's physical address.
    /// If not provided, a realistic address will be auto-generated.
    /// </summary>
    public OrganizationAddress? Address { get; init; }

    /// <summary>
    /// Gets whether the organization is active.
    /// Defaults to true.
    /// </summary>
    public bool Active { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to set this as the current organization in the context.
    /// Defaults to true.
    /// </summary>
    public bool SetAsCurrent { get; init; } = true;

    /// <summary>
    /// Creates an Organization resource and adds it to the scenario context.
    /// Uses OrganizationBuilder internally with scenario-specific orchestration.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);

        // Create builder with schema provider
        var builder = OrganizationBuilder.Create(faker.SchemaProvider);

        // Set required fields
        builder.WithName(OrganizationName);
        builder.WithActive(Active);

        // Apply tag from scenario context
        if (faker.Tag is not null)
        {
            builder.WithTag(faker.Tag);
        }

        // Set type
        if (Type is not null)
        {
            builder.WithType(Type.Code, Type.System, Type.Display);
        }

        // Set identifiers (NPI and Tax ID)
        // OrganizationBuilder auto-generates NPI and Tax ID, but we override if provided
        if (NpiNumber is not null)
        {
            builder.WithNpi(NpiNumber);
        }

        if (TaxId is not null)
        {
            builder.WithTaxId(TaxId);
        }

        // Add custom identifiers
        if (CustomIdentifiers is not null)
        {
            foreach (var (system, value) in CustomIdentifiers)
            {
                builder.WithIdentifier(value, system);
            }
        }

        // Set telecom
        var phoneNumber = Phone ?? GeneratePhoneNumber();
        builder.WithPhone(phoneNumber);

        var emailAddress = Email ?? GenerateEmail();
        builder.WithEmail(emailAddress);

        // Set address
        var address = Address ?? GenerateAddress();
        builder.WithAddress(address.Line, address.City, address.State, address.PostalCode, address.Country);

        // Build the resource
        var organization = builder.Build();

        // Add to context
        context.AddOrganization(organization, OrganizationName, SetAsCurrent);

        // Register with StateId for cross-references
        context.RegisterStateResource(StateId, organization);
    }

    #region NPI and Tax ID Generation

    /// <summary>
    /// The system URI for NPI identifiers.
    /// </summary>
    public const string NpiSystem = "http://hl7.org/fhir/sid/us-npi";

    /// <summary>
    /// The system URI for Tax ID (EIN) identifiers.
    /// </summary>
    public const string TaxIdSystem = "urn:oid:2.16.840.1.113883.4.4";

    /// <summary>
    /// Generates a valid Type 2 NPI (National Provider Identifier) for organizations.
    /// </summary>
    /// <remarks>
    /// Type 2 NPIs are assigned to organizations and begin with the digit "2".
    /// The format is 2XXXXXXXX + Luhn check digit (10 digits total).
    /// </remarks>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public static string GenerateNpi()
    {
        // Type 2 NPIs start with "2" (organizations)
        // Format: 2XXXXXXXX + check digit = 10 digits total
        var baseNumber = "2" + StaticFaker.Random.String2(8, "0123456789");
        var checkDigit = CalculateLuhnCheckDigit(baseNumber);
        return baseNumber + checkDigit;
    }

    /// <summary>
    /// Generates a valid Type 1 NPI (National Provider Identifier) for individual practitioners.
    /// </summary>
    /// <remarks>
    /// Type 1 NPIs are assigned to individual practitioners and begin with the digit "1".
    /// The format is 1XXXXXXXX + Luhn check digit (10 digits total).
    /// </remarks>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public static string GenerateType1Npi()
    {
        // Type 1 NPIs start with "1" (individuals)
        var baseNumber = "1" + StaticFaker.Random.String2(8, "0123456789");
        var checkDigit = CalculateLuhnCheckDigit(baseNumber);
        return baseNumber + checkDigit;
    }

    /// <summary>
    /// Validates an NPI using the Luhn algorithm.
    /// </summary>
    /// <param name="npi">The NPI to validate.</param>
    /// <returns>True if the NPI is valid; otherwise, false.</returns>
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

        // NPI must start with 1 or 2
        if (npi[0] != '1' && npi[0] != '2')
        {
            return false;
        }

        // The NPI uses a modified Luhn algorithm
        // The prefix "80840" is prepended for the check digit calculation
        var prefixedNpi = "80840" + npi;
        return ValidateLuhn(prefixedNpi);
    }

    /// <summary>
    /// Calculates the Luhn check digit for an NPI base number.
    /// </summary>
    /// <remarks>
    /// The NPI uses a modified Luhn algorithm where the prefix "80840" is prepended
    /// before calculating the check digit.
    /// </remarks>
    private static char CalculateLuhnCheckDigit(string baseNumber)
    {
        // NPI check digit is calculated with "80840" prefix
        var prefixedNumber = "80840" + baseNumber;
        var sum = 0;
        var isDouble = true; // Start doubling from rightmost digit

        for (var i = prefixedNumber.Length - 1; i >= 0; i--)
        {
            var digit = prefixedNumber[i] - '0';

            if (isDouble)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            isDouble = !isDouble;
        }

        var checkDigit = (10 - (sum % 10)) % 10;
        return (char)('0' + checkDigit);
    }

    /// <summary>
    /// Validates a number using the Luhn algorithm.
    /// </summary>
    private static bool ValidateLuhn(string number)
    {
        var sum = 0;
        var isDouble = false;

        for (var i = number.Length - 1; i >= 0; i--)
        {
            var digit = number[i] - '0';

            if (isDouble)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            isDouble = !isDouble;
        }

        return sum % 10 == 0;
    }

    /// <summary>
    /// Generates a synthetic Tax ID (EIN/TIN).
    /// </summary>
    /// <remarks>
    /// Format: XX-XXXXXXX (9 digits total).
    /// Uses synthetic prefixes that are not used by the IRS to avoid generating real EINs.
    /// </remarks>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public static string GenerateTaxId()
    {
        // Use prefixes that are known not to be used by the IRS (00-06, 07-09, 17-19, 00)
        // We'll use 00-06 range for synthetic EINs
        var prefix = StaticFaker.Random.Int(0, 6).ToString("D2");
        var suffix = StaticFaker.Random.Int(0, 9999999).ToString("D7");
        return $"{prefix}-{suffix}";
    }

    /// <summary>
    /// Validates a Tax ID format.
    /// </summary>
    /// <param name="taxId">The Tax ID to validate.</param>
    /// <returns>True if the format is valid (XX-XXXXXXX); otherwise, false.</returns>
    public static bool ValidateTaxIdFormat(string taxId)
    {
        if (string.IsNullOrEmpty(taxId) || taxId.Length != 10)
        {
            return false;
        }

        if (taxId[2] != '-')
        {
            return false;
        }

        var digits = taxId.Remove(2, 1); // Remove the hyphen
        return digits.Length == 9 && digits.All(char.IsDigit);
    }

    #endregion

    #region Address and Contact Generation

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private OrganizationAddress GenerateAddress()
    {
        var city = _demographics.Cities[_faker.Random.Int(0, _demographics.Cities.Count - 1)];
        var zipCode = _demographics.SampleZipCode(city);
        var streetNumber = _faker.Random.Int(100, 9999);
        var streetName = _faker.Address.StreetName();
        var streetSuffix = _faker.Random.Bool() ? "Suite " + _faker.Random.Int(100, 999) : null;
        var line = streetSuffix is not null
            ? $"{streetNumber} {streetName}, {streetSuffix}"
            : $"{streetNumber} {streetName}";

        return new OrganizationAddress(
            Line: line,
            City: city.Name,
            State: city.State,
            PostalCode: zipCode,
            Country: "USA"
        );
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private string GeneratePhoneNumber()
    {
        var city = _demographics.Cities[_faker.Random.Int(0, _demographics.Cities.Count - 1)];
        var areaCode = _demographics.SampleAreaCode(city);
        var exchange = _faker.Random.Int(200, 999);
        var subscriber = _faker.Random.Int(0, 9999);
        return $"({areaCode}) {exchange:D3}-{subscriber:D4}";
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    [SuppressMessage("Globalization", "CA1308:Replace ToLowerInvariant with ToUpperInvariant", Justification = "Email addresses are conventionally lowercase")]
    private string GenerateEmail()
    {
        var domain = OrganizationName.ToLowerInvariant()
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace(",", "", StringComparison.Ordinal)
            .Replace(".", "", StringComparison.Ordinal)
            .Replace("'", "", StringComparison.Ordinal)
            .Replace("&", "and", StringComparison.Ordinal);

        // Truncate if too long
        if (domain.Length > 20)
        {
            domain = domain[..20];
        }

        return $"info@{domain}.com";
    }

    #endregion

    #region Organization Types

    /// <summary>
    /// Organization type codes from http://terminology.hl7.org/CodeSystem/organization-type.
    /// </summary>
    public static class OrganizationTypes
    {
        private const string System = "http://terminology.hl7.org/CodeSystem/organization-type";

        /// <summary>Healthcare Provider organization.</summary>
        public static readonly FhirCode HealthcareProvider = new(System, "prov", "Healthcare Provider");

        /// <summary>Hospital Department.</summary>
        public static readonly FhirCode Department = new(System, "dept", "Hospital Department");

        /// <summary>Organizational team.</summary>
        public static readonly FhirCode Team = new(System, "team", "Organizational team");

        /// <summary>Government organization.</summary>
        public static readonly FhirCode Government = new(System, "govt", "Government");

        /// <summary>Insurance Company.</summary>
        public static readonly FhirCode InsuranceCompany = new(System, "ins", "Insurance Company");

        /// <summary>Payer organization.</summary>
        public static readonly FhirCode Payer = new(System, "pay", "Payer");

        /// <summary>Educational Institute.</summary>
        public static readonly FhirCode Educational = new(System, "edu", "Educational Institute");

        /// <summary>Religious Institution.</summary>
        public static readonly FhirCode Religious = new(System, "reli", "Religious Institution");

        /// <summary>Clinical Research Sponsor.</summary>
        public static readonly FhirCode ClinicalResearchSponsor = new(System, "crs", "Clinical Research Sponsor");

        /// <summary>Community Group.</summary>
        public static readonly FhirCode CommunityGroup = new(System, "cg", "Community Group");

        /// <summary>Non-Healthcare Business.</summary>
        public static readonly FhirCode Business = new(System, "bus", "Non-Healthcare Business");

        /// <summary>Other type of organization.</summary>
        public static readonly FhirCode Other = new(System, "other", "Other");
    }

    #endregion

    #region Factory Methods

    /// <summary>
    /// Creates a hospital organization.
    /// </summary>
    /// <param name="name">Optional hospital name. If not provided, generates a realistic name.</param>
    /// <returns>A configured OrganizationState for a hospital.</returns>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public static OrganizationState Hospital(string? name = null)
    {
        var cityName = GetRandomCityName();
        var hospitalName = name ?? GenerateHospitalName(cityName);

        return new OrganizationState
        {
            Name = "Organization_Hospital",
            OrganizationName = hospitalName,
            Type = OrganizationTypes.HealthcareProvider
        };
    }

    /// <summary>
    /// Creates a family practice clinic organization.
    /// </summary>
    /// <param name="name">Optional clinic name. If not provided, generates a realistic name.</param>
    /// <returns>A configured OrganizationState for a family practice clinic.</returns>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public static OrganizationState ClinicFamilyPractice(string? name = null)
    {
        var cityName = GetRandomCityName();
        var clinicName = name ?? GenerateFamilyClinicName(cityName);

        return new OrganizationState
        {
            Name = "Organization_FamilyPractice",
            OrganizationName = clinicName,
            Type = OrganizationTypes.HealthcareProvider
        };
    }

    /// <summary>
    /// Creates an emergency department organization.
    /// </summary>
    /// <param name="name">Optional department name. If not provided, generates a realistic name.</param>
    /// <returns>A configured OrganizationState for an emergency department.</returns>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public static OrganizationState EmergencyDepartment(string? name = null)
    {
        var cityName = GetRandomCityName();
        var deptName = name ?? $"{cityName} General Hospital - Emergency Department";

        return new OrganizationState
        {
            Name = "Organization_EmergencyDepartment",
            OrganizationName = deptName,
            Type = OrganizationTypes.Department
        };
    }

    /// <summary>
    /// Creates an insurance company organization.
    /// </summary>
    /// <param name="name">Optional company name. If not provided, generates a realistic name.</param>
    /// <returns>A configured OrganizationState for an insurance company.</returns>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public static OrganizationState InsuranceCompany(string? name = null)
    {
        var insurerName = name ?? GenerateInsuranceCompanyName();

        return new OrganizationState
        {
            Name = "Organization_InsuranceCompany",
            OrganizationName = insurerName,
            Type = OrganizationTypes.InsuranceCompany
        };
    }

    /// <summary>
    /// Creates a clinical laboratory organization.
    /// </summary>
    /// <param name="name">Optional lab name. If not provided, generates a realistic name.</param>
    /// <returns>A configured OrganizationState for a clinical laboratory.</returns>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public static OrganizationState Laboratory(string? name = null)
    {
        var cityName = GetRandomCityName();
        var labName = name ?? GenerateLaboratoryName(cityName);

        return new OrganizationState
        {
            Name = "Organization_Laboratory",
            OrganizationName = labName,
            Type = OrganizationTypes.HealthcareProvider
        };
    }

    /// <summary>
    /// Creates a pharmacy chain organization.
    /// </summary>
    /// <param name="name">Optional pharmacy name. If not provided, generates a realistic name.</param>
    /// <returns>A configured OrganizationState for a pharmacy.</returns>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public static OrganizationState PharmacyChain(string? name = null)
    {
        var pharmacyName = name ?? GeneratePharmacyName();

        return new OrganizationState
        {
            Name = "Organization_Pharmacy",
            OrganizationName = pharmacyName,
            Type = OrganizationTypes.HealthcareProvider
        };
    }

    /// <summary>
    /// Creates an imaging center organization.
    /// </summary>
    /// <param name="name">Optional center name. If not provided, generates a realistic name.</param>
    /// <returns>A configured OrganizationState for an imaging center.</returns>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public static OrganizationState ImagingCenter(string? name = null)
    {
        var cityName = GetRandomCityName();
        var centerName = name ?? GenerateImagingCenterName(cityName);

        return new OrganizationState
        {
            Name = "Organization_ImagingCenter",
            OrganizationName = centerName,
            Type = OrganizationTypes.HealthcareProvider
        };
    }

    /// <summary>
    /// Creates a payer organization (e.g., Medicare, Medicaid).
    /// </summary>
    /// <param name="name">Optional payer name. If not provided, generates a realistic name.</param>
    /// <returns>A configured OrganizationState for a payer.</returns>
    public static OrganizationState Payer(string? name = null)
    {
        var payerName = name ?? GeneratePayerName();

        return new OrganizationState
        {
            Name = "Organization_Payer",
            OrganizationName = payerName,
            Type = OrganizationTypes.Payer
        };
    }

    /// <summary>
    /// Creates an urgent care clinic organization.
    /// </summary>
    /// <param name="name">Optional clinic name. If not provided, generates a realistic name.</param>
    /// <returns>A configured OrganizationState for an urgent care clinic.</returns>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public static OrganizationState UrgentCare(string? name = null)
    {
        var cityName = GetRandomCityName();
        var clinicName = name ?? GenerateUrgentCareName(cityName);

        return new OrganizationState
        {
            Name = "Organization_UrgentCare",
            OrganizationName = clinicName,
            Type = OrganizationTypes.HealthcareProvider
        };
    }

    /// <summary>
    /// Creates a specialty clinic organization.
    /// </summary>
    /// <param name="specialty">The medical specialty (e.g., "Cardiology", "Orthopedics").</param>
    /// <param name="name">Optional clinic name. If not provided, generates a realistic name.</param>
    /// <returns>A configured OrganizationState for a specialty clinic.</returns>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public static OrganizationState SpecialtyClinic(string specialty, string? name = null)
    {
        var cityName = GetRandomCityName();
        var clinicName = name ?? $"{cityName} {specialty} Associates";

        return new OrganizationState
        {
            Name = $"Organization_{specialty}Clinic",
            OrganizationName = clinicName,
            Type = OrganizationTypes.HealthcareProvider
        };
    }

    #endregion

    #region Name Generation Helpers

    private static readonly DemographicsDataProvider StaticDemographics = DemographicsDataProvider.CreateDefault();

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private static string GetRandomCityName()
    {
        var cities = StaticDemographics.Cities;
        return cities[StaticFaker.Random.Int(0, cities.Count - 1)].Name;
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private static string GenerateHospitalName(string cityName)
    {
        var suffixes = new[]
        {
            "General Hospital",
            "Medical Center",
            "Regional Medical Center",
            "Community Hospital",
            "Memorial Hospital",
            "University Hospital",
            "Health System"
        };

        var prefixes = new[]
        {
            cityName,
            $"St. {StaticFaker.Name.FirstName()}",
            $"{cityName} Regional",
            $"Metro {cityName}"
        };

        var prefix = prefixes[StaticFaker.Random.Int(0, prefixes.Length - 1)];
        var suffix = suffixes[StaticFaker.Random.Int(0, suffixes.Length - 1)];

        return $"{prefix} {suffix}";
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private static string GenerateFamilyClinicName(string cityName)
    {
        var patterns = new[]
        {
            $"{cityName} Family Medicine",
            $"{cityName} Family Practice",
            $"{StaticFaker.Name.LastName()} Family Health Center",
            $"{cityName} Primary Care Associates",
            $"{StaticFaker.Name.LastName()} & {StaticFaker.Name.LastName()} Family Medicine",
            $"Family Care Clinic of {cityName}"
        };

        return patterns[StaticFaker.Random.Int(0, patterns.Length - 1)];
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private static string GenerateInsuranceCompanyName()
    {
        var names = new[]
        {
            "Blue Cross Blue Shield",
            "Aetna Health Insurance",
            "UnitedHealthcare",
            "Cigna Healthcare",
            "Humana Insurance",
            "Kaiser Permanente",
            "Anthem Blue Cross",
            "Centene Corporation",
            "Molina Healthcare",
            "WellCare Health Plans"
        };

        return names[StaticFaker.Random.Int(0, names.Length - 1)];
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private static string GenerateLaboratoryName(string cityName)
    {
        var patterns = new[]
        {
            $"{cityName} Clinical Laboratory",
            "Quest Diagnostics",
            "LabCorp",
            $"{cityName} Medical Laboratory",
            $"Regional Diagnostics - {cityName}",
            $"{StaticFaker.Name.LastName()} Laboratory Services"
        };

        return patterns[StaticFaker.Random.Int(0, patterns.Length - 1)];
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private static string GeneratePharmacyName()
    {
        var names = new[]
        {
            "CVS Pharmacy",
            "Walgreens",
            "Rite Aid",
            "Walmart Pharmacy",
            "Kroger Pharmacy",
            "Costco Pharmacy",
            "Publix Pharmacy",
            "Safeway Pharmacy",
            "Albertsons Pharmacy",
            "Target Pharmacy"
        };

        return names[StaticFaker.Random.Int(0, names.Length - 1)];
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private static string GenerateImagingCenterName(string cityName)
    {
        var patterns = new[]
        {
            $"{cityName} Imaging Center",
            $"{cityName} Radiology Associates",
            $"Advanced Imaging of {cityName}",
            $"{cityName} Diagnostic Imaging",
            $"MRI & CT Center of {cityName}",
            $"{StaticFaker.Name.LastName()} Radiology"
        };

        return patterns[StaticFaker.Random.Int(0, patterns.Length - 1)];
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private static string GeneratePayerName()
    {
        var names = new[]
        {
            "Medicare",
            "Medicaid",
            "TRICARE",
            "Veterans Health Administration",
            "Federal Employees Health Benefits",
            "State Health Insurance Program"
        };

        return names[StaticFaker.Random.Int(0, names.Length - 1)];
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private static string GenerateUrgentCareName(string cityName)
    {
        var patterns = new[]
        {
            $"{cityName} Urgent Care",
            $"FastMed Urgent Care - {cityName}",
            $"CityMD Urgent Care - {cityName}",
            $"{cityName} Walk-In Clinic",
            $"Concentra Urgent Care - {cityName}",
            $"MedExpress Urgent Care - {cityName}"
        };

        return patterns[StaticFaker.Random.Int(0, patterns.Length - 1)];
    }

    #endregion
}

/// <summary>
/// Represents an organization's physical address.
/// </summary>
/// <param name="Line">Street address line.</param>
/// <param name="City">City name.</param>
/// <param name="State">State name.</param>
/// <param name="PostalCode">Postal/ZIP code.</param>
/// <param name="Country">Country code (default: USA).</param>
public record OrganizationAddress(
    string Line,
    string City,
    string State,
    string PostalCode,
    string Country = "USA"
);

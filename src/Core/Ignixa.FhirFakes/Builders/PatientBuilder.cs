// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Bogus;
using Ignixa.FhirFakes.Builders.Profiles;
using Ignixa.FhirFakes.Population;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Builders;

/// <summary>
/// Fluent builder for generating realistic Patient resources with sophisticated demographics.
/// Provides consistent patient generation across scenarios, tests, and population simulation.
/// </summary>
/// <remarks>
/// Supports two modes:
/// 1. Simple mode: Basic Bogus-based randomization (suitable for simple tests)
/// 2. Realistic mode: Real US demographics with ethnically appropriate names (suitable for population generation)
///
/// Example Usage:
/// <code>
/// // Simple mode
/// var patient = PatientBuilderFactory.Create(schemaProvider)
///     .WithAge(45)
///     .WithGender("male")
///     .WithGivenName("John")
///     .WithFamilyName("Smith")
///     .Build();
///
/// // Realistic mode
/// var patient = PatientBuilderFactory.Create(schemaProvider)
///     .FromCity("Boston", "Massachusetts")  // Auto: race, age, gender, zip, area code, name
///     .WithAge(45)                          // Override age if desired
///     .Build();
/// </code>
/// </remarks>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class PatientBuilder : FhirResourceBuilder<PatientBuilder>
{
    private readonly Faker _faker = new();
    private readonly LocalBasedNameGenerator? _nameGenerator;
    private readonly DemographicsDataProvider? _demographics;

    // Demographic configuration
    private int? _age;
    private int? _birthYear;
    private int? _birthMonth;
    private int? _birthDay;
    private string? _birthDateString; // For direct FHIR date string assignment (supports year/month/day precision)
    private string? _gender;
    private string? _givenName;
    private string? _familyName;

    // Geographic configuration
    private string? _city;
    private string? _state;
    private string? _country;
    private string? _zipCode;
    private string? _areaCode;
    private string? _streetAddress;

    // Clinical configuration
    private decimal? _bmi;
    private bool _active = true;

    // Multiple birth configuration
    private int? _multipleBirthInteger;
    private bool? _multipleBirthBoolean;

    // Reference configuration
    private string? _managingOrganizationId;
    private readonly List<GeneralPractitionerReference> _generalPractitioners = [];

    // Identifier configuration
    private readonly List<IdentifierConfig> _identifiers = [];

    // Additional names (beyond the primary official name)
    private readonly List<AdditionalName> _additionalNames = [];

    // Profile-specific configuration (Attributes Pattern)
    private IPatientProfile _profile = DefaultPatientProfile.Instance;
    private readonly Dictionary<string, object> _profileAttributes = new();

    // === Public Read-Only Access to Configured Values ===
    // These enable PopulationGenerator and other consumers to access sampled demographics

    /// <summary>
    /// Gets the configured or sampled age, if set.
    /// </summary>
    public int? Age => _age;

    /// <summary>
    /// Gets the configured or sampled gender, if set.
    /// </summary>
    public string? Gender => _gender;

    /// <summary>
    /// Gets the configured or sampled zip code, if set.
    /// </summary>
    public string? ZipCode => _zipCode;

    /// <summary>
    /// Gets the configured or sampled area code, if set.
    /// </summary>
    public string? AreaCode => _areaCode;

    /// <summary>
    /// Gets the configured or sampled given name, if set.
    /// </summary>
    public string? GivenName => _givenName;

    /// <summary>
    /// Gets the configured or sampled family name, if set.
    /// </summary>
    public string? FamilyName => _familyName;

    /// <summary>
    /// Gets the configured BMI, if set.
    /// </summary>
    public decimal? BMI => _bmi;

    /// <summary>
    /// Gets the current patient profile.
    /// </summary>
    public IPatientProfile Profile => _profile;

    /// <summary>
    /// Gets a read-only view of the profile-specific attributes.
    /// </summary>
    public IReadOnlyDictionary<string, object> ProfileAttributes => _profileAttributes;

    /// <summary>
    /// Creates a simple builder with basic Bogus-based randomization.
    /// </summary>
    public PatientBuilder(IFhirSchemaProvider schemaProvider)
        : base(schemaProvider)
    {
    }

    /// <summary>
    /// Creates a sophisticated builder with real US demographics and ethnic name generation.
    /// </summary>
    public PatientBuilder(
        IFhirSchemaProvider schemaProvider,
        DemographicsDataProvider demographics,
        LocalBasedNameGenerator nameGenerator)
        : base(schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(demographics);
        ArgumentNullException.ThrowIfNull(nameGenerator);

        _demographics = demographics;
        _nameGenerator = nameGenerator;
    }

    // === Demographic Configuration ===

    /// <summary>
    /// Sets the patient's age in years.
    /// </summary>
    public PatientBuilder WithAge(int age)
    {
        _age = age;
        return this;
    }

    /// <summary>
    /// Sets the patient's birth year directly.
    /// </summary>
    public PatientBuilder WithBirthYear(int year)
    {
        _birthYear = year;
        _birthMonth = null;
        _birthDay = null;
        _birthDateString = null;
        return this;
    }

    /// <summary>
    /// Sets the patient's birthdate with year-only precision.
    /// Stores as FHIR date string "YYYY" (e.g., "1982").
    /// </summary>
    /// <param name="year">Birth year (1900-2100)</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var patient = CreatePatient()
    ///     .WithBirthDate(1982)  // Year-only precision
    ///     .Build();
    /// // birthDate will be "1982"
    /// </code>
    /// </example>
    public PatientBuilder WithBirthDate(int year)
    {
        ValidateYear(year);
        _birthDateString = year.ToString();
        _birthYear = year;
        _birthMonth = null;
        _birthDay = null;
        _age = CalculateAge(year);
        return this;
    }

    /// <summary>
    /// Sets the patient's birthdate with month-only precision.
    /// Stores as FHIR date string "YYYY-MM" (e.g., "1982-01").
    /// </summary>
    /// <param name="year">Birth year (1900-2100)</param>
    /// <param name="month">Birth month (1-12)</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var patient = CreatePatient()
    ///     .WithBirthDate(1982, 1)  // Month precision: January 1982
    ///     .Build();
    /// // birthDate will be "1982-01"
    /// </code>
    /// </example>
    public PatientBuilder WithBirthDate(int year, int month)
    {
        ValidateYear(year);
        ValidateMonth(month);
        _birthDateString = $"{year:D4}-{month:D2}";
        _birthYear = year;
        _birthMonth = month;
        _birthDay = null;
        _age = CalculateAge(year, month);
        return this;
    }

    /// <summary>
    /// Sets the patient's birthdate with full date precision.
    /// Stores as FHIR date string "YYYY-MM-DD" (e.g., "1982-01-15").
    /// </summary>
    /// <param name="year">Birth year (1900-2100)</param>
    /// <param name="month">Birth month (1-12)</param>
    /// <param name="day">Birth day (1-31, validated for month)</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var patient = CreatePatient()
    ///     .WithBirthDate(1982, 1, 15)  // Full date precision
    ///     .Build();
    /// // birthDate will be "1982-01-15"
    /// </code>
    /// </example>
    public PatientBuilder WithBirthDate(int year, int month, int day)
    {
        ValidateYear(year);
        ValidateMonth(month);
        ValidateDay(year, month, day);
        _birthDateString = $"{year:D4}-{month:D2}-{day:D2}";
        _birthYear = year;
        _birthMonth = month;
        _birthDay = day;
        _age = CalculateAge(year, month, day);
        return this;
    }

    /// <summary>
    /// Sets the patient's gender using a selector for better discoverability.
    /// </summary>
    /// <param name="selector">Selector function to choose gender (e.g., g => g.Male)</param>
    /// <example>
    /// <code>
    /// .WithGender(g => g.Male)
    /// .WithGender(g => g.Female)
    /// </code>
    /// </example>
    public PatientBuilder WithGender(Func<PatientBuilderSelectors.Gender, string> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _gender = selector(new PatientBuilderSelectors.Gender());
        return this;
    }

    /// <summary>
    /// Sets the patient's gender (e.g., "male", "female", "other", "unknown").
    /// </summary>
    public PatientBuilder WithGender(string gender)
    {
        ArgumentNullException.ThrowIfNull(gender);
        _gender = gender;
        return this;
    }

    /// <summary>
    /// Sets the patient's given (first) name.
    /// </summary>
    public PatientBuilder WithGivenName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _givenName = name;
        return this;
    }

    /// <summary>
    /// Sets the patient's family (last) name.
    /// </summary>
    public PatientBuilder WithFamilyName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _familyName = name;
        return this;
    }

    /// <summary>
    /// Adds an additional name to the patient (e.g., nickname, maiden name).
    /// The primary name is set via WithGivenName/WithFamilyName. This method adds secondary names.
    /// </summary>
    /// <param name="family">The family (last) name for this additional name.</param>
    /// <param name="given">The given (first) name for this additional name.</param>
    /// <param name="use">The name use (e.g., "nickname", "maiden", "old"). Defaults to "nickname".</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var patient = CreatePatient()
    ///     .WithGivenName("John")
    ///     .WithFamilyName("Smith")
    ///     .AddName("Johnny", "Smith", "nickname")
    ///     .AddName("Smithson", "John", "maiden")
    ///     .Build();
    /// </code>
    /// </example>
    public PatientBuilder AddName(string family, string given, string use = "nickname")
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(given);
        ArgumentNullException.ThrowIfNull(use);
        _additionalNames.Add(new AdditionalName(family, given, use));
        return this;
    }

    /// <summary>
    /// Sets a profile-specific attribute.
    /// Use this for country-specific demographics not covered by dedicated methods.
    /// </summary>
    /// <param name="key">Attribute key (e.g., "indigenousStatus", "ethnicity")</param>
    /// <param name="value">Attribute value</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// // Australian indigenous status
    /// builder.WithAttribute("indigenousStatus", "4");
    ///
    /// // Custom attribute
    /// builder.WithAttribute("customDemographic", someValue);
    /// </code>
    /// </example>
    public PatientBuilder WithAttribute(string key, object value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        _profileAttributes[key] = value;
        return this;
    }

    /// <summary>
    /// Sets the patient profile explicitly.
    /// </summary>
    /// <param name="profile">The profile to use for extension generation</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// builder.WithProfile(PatientProfileFactory.AUBase)
    ///        .WithAttribute("indigenousStatus", "4");
    /// </code>
    /// </example>
    public PatientBuilder WithProfile(IPatientProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profile = profile;
        return this;
    }

    // === Geographic Configuration ===

    /// <summary>
    /// Sets the patient's city.
    /// </summary>
    public PatientBuilder WithCity(string city)
    {
        ArgumentNullException.ThrowIfNull(city);
        _city = city;
        return this;
    }

    /// <summary>
    /// Sets the patient's state.
    /// </summary>
    public PatientBuilder WithState(string state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        return this;
    }

    /// <summary>
    /// Sets the patient's ZIP code.
    /// </summary>
    public PatientBuilder WithZipCode(string zip)
    {
        ArgumentNullException.ThrowIfNull(zip);
        _zipCode = zip;
        return this;
    }

    /// <summary>
    /// Sets the patient's area code for phone number generation.
    /// </summary>
    public PatientBuilder WithAreaCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        _areaCode = code;
        return this;
    }

    /// <summary>
    /// Sets the patient's full address with all components.
    /// </summary>
    public PatientBuilder WithAddress(string street, string city, string state, string zip)
    {
        ArgumentNullException.ThrowIfNull(street);
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(zip);

        _streetAddress = street;
        _city = city;
        _state = state;
        _zipCode = zip;
        return this;
    }

    // === Clinical Configuration ===

    /// <summary>
    /// Sets the patient's BMI (Body Mass Index).
    /// </summary>
    public PatientBuilder WithBMI(decimal bmi)
    {
        _bmi = bmi;
        return this;
    }

    /// <summary>
    /// Sets whether the patient's record is active.
    /// </summary>
    public PatientBuilder WithActive(bool active)
    {
        _active = active;
        return this;
    }

    /// <summary>
    /// Sets multipleBirthInteger to indicate birth order in multiple birth.
    /// Used for number searches with comparison operators.
    /// </summary>
    /// <param name="order">Birth order (1 = first born, 2 = second, etc.). Must be positive.</param>
    /// <returns>This builder for method chaining</returns>
    /// <exception cref="ArgumentException">Thrown when order is less than 1</exception>
    /// <example>
    /// <code>
    /// var triplet = CreatePatient()
    ///     .WithMultipleBirth(3)  // Third born of triplets
    ///     .Build();
    /// </code>
    /// </example>
    public PatientBuilder WithMultipleBirth(int order)
    {
        if (order < 1)
        {
            throw new ArgumentException("Birth order must be positive", nameof(order));
        }

        _multipleBirthInteger = order;
        _multipleBirthBoolean = null; // Clear boolean variant
        return this;
    }

    /// <summary>
    /// Sets multipleBirthBoolean to indicate if patient is part of multiple birth.
    /// </summary>
    /// <param name="isMultipleBirth">True if patient is a twin/triplet/etc., false if singleton.</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var singleton = CreatePatient()
    ///     .WithMultipleBirth(false)  // Not a multiple birth
    ///     .Build();
    /// </code>
    /// </example>
    public PatientBuilder WithMultipleBirth(bool isMultipleBirth)
    {
        _multipleBirthBoolean = isMultipleBirth;
        _multipleBirthInteger = null; // Clear integer variant
        return this;
    }

    // === Reference Configuration ===

    /// <summary>
    /// Sets the patient's managing organization reference.
    /// </summary>
    /// <param name="organizationId">The organization resource ID (not the full reference path).</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var patient = CreatePatient()
    ///     .WithManagingOrganization(organization.Id!)
    ///     .Build();
    /// </code>
    /// </example>
    public PatientBuilder WithManagingOrganization(string organizationId)
    {
        ArgumentNullException.ThrowIfNull(organizationId);
        _managingOrganizationId = organizationId;
        return this;
    }

    /// <summary>
    /// Adds a general practitioner reference (defaults to Practitioner resource type).
    /// </summary>
    /// <param name="practitionerId">The ID of the practitioner.</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var patient = CreatePatient()
    ///     .WithGeneralPractitioner(practitioner.Id!)
    ///     .Build();
    /// </code>
    /// </example>
    public PatientBuilder WithGeneralPractitioner(string practitionerId)
    {
        ArgumentNullException.ThrowIfNull(practitionerId);
        return WithGeneralPractitioner("Practitioner", practitionerId);
    }

    /// <summary>
    /// Adds a general practitioner reference with explicit resource type.
    /// </summary>
    /// <param name="resourceType">The resource type (e.g., "Practitioner", "Organization").</param>
    /// <param name="id">The resource ID.</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var patient = CreatePatient()
    ///     .WithGeneralPractitioner("Practitioner", practitioner.Id!)
    ///     .WithGeneralPractitioner("Organization", organization.Id!)
    ///     .Build();
    /// </code>
    /// </example>
    public PatientBuilder WithGeneralPractitioner(string resourceType, string id)
    {
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(id);
        _generalPractitioners.Add(new GeneralPractitionerReference(resourceType, id));
        return this;
    }

    // === Smart Defaults ===

    /// <summary>
    /// Samples demographics from a city using a CityDemographics instance.
    /// Automatically sets: profile-specific attributes, age, gender, name, zip, area code, city, state, country.
    /// The appropriate profile (US Core, AU Base, etc.) is automatically selected based on the city's country.
    /// </summary>
    /// <param name="city">The city demographics (use KnownCities class for predefined cities)</param>
    /// <returns>This builder for method chaining</returns>
    /// <exception cref="InvalidOperationException">Thrown if DemographicsDataProvider was not provided in constructor</exception>
    /// <example>
    /// <code>
    /// // Use KnownCities for predefined cities (US cities get US Core profile)
    /// var patient = PatientBuilderFactory.Create(schemaProvider)
    ///     .FromCity(KnownCities.Boston)
    ///     .WithAge(45)
    ///     .Build();
    ///
    /// // International cities get appropriate profile (AU cities get AU Base profile)
    /// var patient = PatientBuilderFactory.Create(schemaProvider)
    ///     .FromCity(KnownCities.Melbourne)
    ///     .Build();
    /// </code>
    /// </example>
    public PatientBuilder FromCity(CityDemographics city)
    {
        ArgumentNullException.ThrowIfNull(city);

        if (_demographics == null)
        {
            throw new InvalidOperationException(
                "DemographicsDataProvider required for FromCity(). " +
                "Use PatientBuilderFactory.Create() to create a builder with demographics support.");
        }

        // Set the appropriate profile based on country
        _profile = city.GetProfile();

        // Sample profile-specific attributes
        var sampledAttributes = _demographics.SampleProfileAttributes(city);
        foreach (var (key, value) in sampledAttributes)
        {
            _profileAttributes[key] = value;
        }

        // Sample core demographics
        _age = _demographics.SampleAge(city);
        _gender = _demographics.SampleGender(city);
        _zipCode = _demographics.SampleZipCode(city);
        _areaCode = _demographics.SampleAreaCode(city);
        _city = city.Name;
        _state = city.State;
        _country = city.Country;

        return this;
    }

    /// <summary>
    /// Generates a patient from Seattle, Washington with Pacific Northwest demographics.
    /// Automatically sets: ethnicity, age, gender, name, zip, area code, city, state.
    /// </summary>
    /// <returns>This builder for method chaining</returns>
    /// <exception cref="InvalidOperationException">Thrown if DemographicsDataProvider was not provided in constructor</exception>
    /// <example>
    /// <code>
    /// var patient = PatientBuilderFactory.Create(schemaProvider)
    ///     .FromSeattle()
    ///     .WithAge(35)  // Optional: override auto-generated age
    ///     .Build();
    /// </code>
    /// </example>
    public PatientBuilder FromSeattle()
    {
        return FromCity(KnownCities.Seattle);
    }

    /// <summary>
    /// Generates culturally appropriate name based on the current profile's name generation strategy.
    /// For US Core profile, names are based on ethnicity; for other profiles, names are based on country.
    /// Requires LocalBasedNameGenerator (use PatientBuilderFactory.Create() for this).
    /// </summary>
    /// <returns>This builder for method chaining</returns>
    /// <exception cref="InvalidOperationException">Thrown if LocalBasedNameGenerator was not provided in constructor</exception>
    public PatientBuilder WithName()
    {
        if (_nameGenerator == null)
        {
            throw new InvalidOperationException(
                "LocalBasedNameGenerator required for WithName(). " +
                "Use PatientBuilderFactory.Create() to create a builder with ethnic name support.");
        }

        var gender = _gender ?? PatientBuilderConstants.Gender.Unknown;
        var (given, family) = _profile.NameGenerationStrategy.GenerateName(
            gender,
            _profileAttributes,
            _profile.CountryCode);
        _givenName = given;
        _familyName = family;

        return this;
    }

    /// <summary>
    /// Generates realistic BMI from US adult distribution (NHANES data).
    /// Distribution: 35% normal (19-24), 34% overweight (25-29), 31% obese (30-42).
    /// </summary>
    /// <returns>This builder for method chaining</returns>
    public PatientBuilder WithRealisticBMI()
    {
        var random = Random.Shared.NextDouble();
        _bmi = random switch
        {
            < 0.35 => (decimal)Random.Shared.Next(19, 25),  // Normal (35%)
            < 0.69 => (decimal)Random.Shared.Next(25, 30),  // Overweight (34%)
            _ => (decimal)Random.Shared.Next(30, 42)        // Obese (31%)
        };
        return this;
    }

    /// <summary>
    /// Builds the Patient resource with all configured properties.
    /// </summary>
    /// <returns>A ResourceJsonNode representing the Patient resource</returns>
    public override ResourceJsonNode Build()
    {
        // Calculate derived fields
        CalculateDerivedFields();

        // Build Patient JSON (extracted from PatientLifecycleGenerator.GeneratePatient)
        var patientJson = new JsonObject
        {
            ["resourceType"] = "Patient",
            ["id"] = Id ?? Guid.NewGuid().ToString(),
            ["meta"] = BuildMeta(),
            ["gender"] = _gender ?? PatientBuilderConstants.Gender.Unknown,
            ["birthDate"] = _birthDateString ?? CalculateBirthDate().ToString("yyyy-MM-dd"),
            ["name"] = BuildName(),
            ["active"] = _active
        };

        // Optional fields
        if (HasAddress())
        {
            patientJson["address"] = BuildAddress();
        }

        if (HasTelecom())
        {
            patientJson["telecom"] = BuildTelecom();
        }

        if (HasExtensions())
        {
            patientJson["extension"] = BuildExtensions();
        }

        if (!string.IsNullOrEmpty(_managingOrganizationId))
        {
            patientJson["managingOrganization"] = new JsonObject
            {
                ["reference"] = $"Organization/{_managingOrganizationId}"
            };
        }

        if (_generalPractitioners.Count > 0)
        {
            var gpArray = new JsonArray();
            foreach (var gp in _generalPractitioners)
            {
                gpArray.Add(new JsonObject
                {
                    ["reference"] = $"{gp.ResourceType}/{gp.Id}"
                });
            }
            patientJson["generalPractitioner"] = gpArray;
        }

        if (_multipleBirthInteger.HasValue)
        {
            patientJson["multipleBirthInteger"] = _multipleBirthInteger.Value;
        }
        else if (_multipleBirthBoolean.HasValue)
        {
            patientJson["multipleBirthBoolean"] = _multipleBirthBoolean.Value;
        }

        if (HasIdentifiers())
        {
            patientJson["identifier"] = BuildIdentifiers();
        }

        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(patientJson);
    }

    // === Private Helper Methods ===

    private void CalculateDerivedFields()
    {
        // Auto-generate names if not provided
        if (_givenName == null || _familyName == null)
        {
            if (_nameGenerator != null)
            {
                // Use profile's name generation strategy if name generator is available
                var gender = _gender ?? PatientBuilderConstants.Gender.Unknown;
                var (given, family) = _profile.NameGenerationStrategy.GenerateName(
                    gender,
                    _profileAttributes,
                    _profile.CountryCode);
                _givenName ??= given;
                _familyName ??= family;
            }
            else
            {
                // Fallback to Bogus
                _givenName ??= _gender == PatientBuilderConstants.Gender.Male
                    ? _faker.Name.FirstName(Bogus.DataSets.Name.Gender.Male)
                    : _gender == PatientBuilderConstants.Gender.Female
                        ? _faker.Name.FirstName(Bogus.DataSets.Name.Gender.Female)
                        : _faker.Name.FirstName();
                _familyName ??= _faker.Name.LastName();
            }
        }

        // Calculate birthYear from age if needed
        if (_birthYear == null && _age != null)
        {
            _birthYear = DateTime.UtcNow.Year - _age.Value;
        }

        // Set default birthYear if neither age nor birthYear provided
        _birthYear ??= DateTime.UtcNow.Year - 30; // Default age 30

        // Auto-generate street address if any address component is provided but no street
        if (_streetAddress == null && (_city != null || _zipCode != null))
        {
            _streetAddress = _faker.Address.StreetAddress();
        }

        // Auto-generate state abbr if not provided but we have a state name
        // (handled in BuildAddress)
    }

    private DateTime CalculateBirthDate()
    {
        var year = _birthYear ?? DateTime.UtcNow.Year - 30;
        return new DateTime(year, 1, 1);
    }

    private JsonArray BuildName()
    {
        var names = new JsonArray
        {
            new JsonObject
            {
                ["use"] = "official",
                ["family"] = _familyName ?? _faker.Name.LastName(),
                ["given"] = new JsonArray(JsonValue.Create(_givenName ?? _faker.Name.FirstName()))
            }
        };

        foreach (var additionalName in _additionalNames)
        {
            names.Add(new JsonObject
            {
                ["use"] = additionalName.Use,
                ["family"] = additionalName.Family,
                ["given"] = new JsonArray(JsonValue.Create(additionalName.Given))
            });
        }

        return names;
    }

    private bool HasAddress()
    {
        return !string.IsNullOrEmpty(_zipCode) || !string.IsNullOrEmpty(_city);
    }

    private JsonArray BuildAddress()
    {
        var addressJson = new JsonObject
        {
            ["use"] = "home",
            ["type"] = "both"
        };

        if (!string.IsNullOrEmpty(_streetAddress))
        {
            addressJson["line"] = new JsonArray(JsonValue.Create(_streetAddress));
        }
        else if (_city != null)
        {
            // Auto-generate street if we have city
            addressJson["line"] = new JsonArray(JsonValue.Create(_faker.Address.StreetAddress()));
        }

        if (!string.IsNullOrEmpty(_city))
        {
            addressJson["city"] = _city;
        }

        if (!string.IsNullOrEmpty(_state))
        {
            // Accept full state name or abbreviation
            addressJson["state"] = _state;
        }

        if (!string.IsNullOrEmpty(_zipCode))
        {
            addressJson["postalCode"] = _zipCode;
        }

        addressJson["country"] = _country ?? "US";

        return [addressJson];
    }

    private bool HasTelecom()
    {
        return !string.IsNullOrEmpty(_areaCode);
    }

    private JsonArray BuildTelecom()
    {
        var phoneNumber = $"{_areaCode}-{Random.Shared.Next(100, 1000)}-{Random.Shared.Next(1000, 10000)}";
        var telecomJson = new JsonObject
        {
            ["system"] = "phone",
            ["value"] = phoneNumber,
            ["use"] = "mobile"
        };

        return [telecomJson];
    }

    private bool HasExtensions()
    {
        // Has extensions if BMI is set OR if profile has any attributes to build extensions from
        return _bmi.HasValue || _profileAttributes.Count > 0;
    }

    private JsonArray BuildExtensions()
    {
        var extensions = new JsonArray();

        // Delegate extension building to the profile
        foreach (var extension in _profile.BuildExtensions(_profileAttributes, _bmi))
        {
            extensions.Add(extension);
        }

        return extensions;
    }

    /// <summary>
    /// Determines if the patient is from the US (for USCore extension eligibility).
    /// </summary>
    private bool IsUSPatient()
    {
        // Country defaults to "US" if not set, so treat null/empty as US
        return string.IsNullOrEmpty(_country) || _country == "US";
    }

    // === Birth Date Validation and Calculation Methods ===

    /// <summary>
    /// Validates that the year is within a reasonable range.
    /// </summary>
    private static void ValidateYear(int year)
    {
        if (year < 1900 || year > 2100)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, "Year must be between 1900 and 2100");
        }
    }

    /// <summary>
    /// Validates that the month is between 1 and 12.
    /// </summary>
    private static void ValidateMonth(int month)
    {
        if (month < 1 || month > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be between 1 and 12");
        }
    }

    /// <summary>
    /// Validates that the day is valid for the given year and month.
    /// </summary>
    private static void ValidateDay(int year, int month, int day)
    {
        if (day < 1 || day > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(day), day, "Day must be between 1 and 31");
        }

        try
        {
            _ = new DateTime(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentOutOfRangeException(nameof(day), day, $"Invalid day for {year}-{month:D2}");
        }
    }

    /// <summary>
    /// Calculates approximate age from year only.
    /// </summary>
    private static int CalculateAge(int year)
    {
        return DateTime.UtcNow.Year - year;
    }

    /// <summary>
    /// Calculates approximate age from year and month.
    /// </summary>
    private static int CalculateAge(int year, int month)
    {
        var today = DateTime.UtcNow;
        var age = today.Year - year;
        if (today.Month < month)
        {
            age--;
        }
        return age;
    }

    /// <summary>
    /// Calculates exact age from full birth date.
    /// </summary>
    private static int CalculateAge(int year, int month, int day)
    {
        var today = DateTime.UtcNow;
        var birthDate = new DateTime(year, month, day);
        var age = today.Year - birthDate.Year;
        if (today < birthDate.AddYears(age))
        {
            age--;
        }
        return age;
    }

    // === Identifier Support ===

    /// <summary>
    /// Adds a typed identifier to the patient.
    /// Creates an identifier with a type code from the FHIR v2-0203 code system.
    /// </summary>
    /// <param name="value">The identifier value (e.g., "12345", "123-45-6789")</param>
    /// <param name="typeSystem">The type coding system (e.g., "http://terminology.hl7.org/CodeSystem/v2-0203")</param>
    /// <param name="typeCode">The type code (e.g., "MR", "SS", "DL", "PPN")</param>
    /// <param name="typeDisplay">Optional display text for the type (e.g., "Medical Record", "Social Security Number")</param>
    /// <param name="identifierSystem">Optional system for the identifier value itself</param>
    /// <returns>This builder for method chaining</returns>
    /// <example>
    /// <code>
    /// // Medical Record Number
    /// var patient = CreatePatient()
    ///     .WithTypedIdentifier("12345", "http://terminology.hl7.org/CodeSystem/v2-0203", "MR", "Medical Record")
    ///     .Build();
    ///
    /// // Social Security Number with identifier system
    /// var patient = CreatePatient()
    ///     .WithTypedIdentifier("123-45-6789", "http://terminology.hl7.org/CodeSystem/v2-0203", "SS", "Social Security Number", "http://hl7.org/fhir/sid/us-ssn")
    ///     .Build();
    /// </code>
    /// </example>
    public PatientBuilder WithTypedIdentifier(
        string value,
        string typeSystem,
        string typeCode,
        string? typeDisplay = null,
        string? identifierSystem = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(typeSystem);
        ArgumentNullException.ThrowIfNull(typeCode);

        _identifiers.Add(new IdentifierConfig
        {
            Value = value,
            TypeSystem = typeSystem,
            TypeCode = typeCode,
            TypeDisplay = typeDisplay,
            IdentifierSystem = identifierSystem
        });

        return this;
    }

    private bool HasIdentifiers()
    {
        return _identifiers.Count > 0;
    }

    private JsonArray BuildIdentifiers()
    {
        var identifierArray = new JsonArray();

        foreach (var config in _identifiers)
        {
            var identifier = new JsonObject
            {
                ["type"] = new JsonObject
                {
                    ["coding"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["system"] = config.TypeSystem,
                            ["code"] = config.TypeCode
                        }
                    }
                },
                ["value"] = config.Value
            };

            // Add type display if provided
            if (!string.IsNullOrEmpty(config.TypeDisplay))
            {
                var typeCoding = identifier["type"]!["coding"]!.AsArray()[0]!.AsObject();
                typeCoding["display"] = config.TypeDisplay;
            }

            // Add identifier system if provided
            if (!string.IsNullOrEmpty(config.IdentifierSystem))
            {
                identifier["system"] = config.IdentifierSystem;
            }

            identifierArray.Add(identifier);
        }

        return identifierArray;
    }

    /// <summary>
    /// Configuration for a typed identifier.
    /// </summary>
    private sealed class IdentifierConfig
    {
        public required string Value { get; init; }
        public required string TypeSystem { get; init; }
        public required string TypeCode { get; init; }
        public string? TypeDisplay { get; init; }
        public string? IdentifierSystem { get; init; }
    }

    /// <summary>
    /// Configuration for an additional name (beyond the primary official name).
    /// </summary>
    private readonly record struct AdditionalName(string Family, string Given, string Use);

    /// <summary>
    /// Configuration for a general practitioner reference.
    /// </summary>
    private readonly record struct GeneralPractitionerReference(string ResourceType, string Id);
}

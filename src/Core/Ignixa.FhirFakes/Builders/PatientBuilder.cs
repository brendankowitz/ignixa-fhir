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
public sealed class PatientBuilder
{
    private readonly IFhirSchemaProvider _schemaProvider;
    private readonly Faker _faker = new();
    private readonly LocalBasedNameGenerator? _nameGenerator;
    private readonly DemographicsDataProvider? _demographics;

    // Demographic configuration
    private int? _age;
    private int? _birthYear;
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

    // ID/Meta configuration
    private string? _id;
    private string? _tag;

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
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        _schemaProvider = schemaProvider;
    }

    /// <summary>
    /// Creates a sophisticated builder with real US demographics and ethnic name generation.
    /// </summary>
    public PatientBuilder(
        IFhirSchemaProvider schemaProvider,
        DemographicsDataProvider demographics,
        LocalBasedNameGenerator nameGenerator)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(demographics);
        ArgumentNullException.ThrowIfNull(nameGenerator);

        _schemaProvider = schemaProvider;
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

    // === Metadata Configuration ===

    /// <summary>
    /// Sets the patient's resource ID.
    /// </summary>
    public PatientBuilder WithId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        _id = id;
        return this;
    }

    /// <summary>
    /// Sets a tag to be included in the patient's meta.tag element.
    /// </summary>
    public PatientBuilder WithTag(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        _tag = tag;
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
    public ResourceJsonNode Build()
    {
        // Calculate derived fields
        CalculateDerivedFields();

        // Build Patient JSON (extracted from PatientLifecycleGenerator.GeneratePatient)
        var patientJson = new JsonObject
        {
            ["resourceType"] = "Patient",
            ["id"] = _id ?? Guid.NewGuid().ToString(),
            ["meta"] = BuildMeta(),
            ["gender"] = _gender ?? PatientBuilderConstants.Gender.Unknown,
            ["birthDate"] = CalculateBirthDate().ToString("yyyy-MM-dd"),
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

        var json = patientJson.ToJsonString();
        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
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

    private JsonObject BuildMeta()
    {
        var meta = new JsonObject
        {
            ["versionId"] = "1",
            ["lastUpdated"] = DateTime.UtcNow.ToString("o")
        };

        if (_tag != null)
        {
            meta["tag"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "http://ignixa.dev/test-isolation",
                    ["code"] = _tag
                }
            };
        }

        return meta;
    }

    private DateTime CalculateBirthDate()
    {
        var year = _birthYear ?? DateTime.UtcNow.Year - 30;
        return new DateTime(year, 1, 1);
    }

    private JsonArray BuildName()
    {
        return
        [
            new JsonObject
            {
                ["use"] = "official",
                ["family"] = _familyName ?? _faker.Name.LastName(),
                ["given"] = new JsonArray(JsonValue.Create(_givenName ?? _faker.Name.FirstName()))
            }
        ];
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
}

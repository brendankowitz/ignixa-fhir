// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.Population;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Abstractions;
using Ignixa.Specification;

namespace Ignixa.Api.E2ETests._TestData.Fixtures.Sorting;

/// <summary>
/// Provides test data for sort tests.
/// Creates patients with varying birthdates, names, and lastUpdated times
/// to test ascending/descending sorts, pagination with sort, and multi-field sorts.
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.SortTests
/// </summary>
public static class SortTestScenario
{
    /// <summary>
    /// Creates test data for basic sort tests (4 patients).
    /// Patients have distinct family names and birthdates for testing basic sort ordering.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="tag">A unique tag for test isolation.</param>
    /// <returns>SortTestData containing all created patients.</returns>
    /// <remarks>
    /// Patients created:
    /// - Robinson: birthdate 90 days ago
    /// - Williams: birthdate 60 days ago
    /// - Williamas: birthdate 40 days ago
    /// - Jones: birthdate 30 days ago
    /// </remarks>
    public static SortTestData GetBasicSortTestData(
        this IFhirSchemaProvider schemaProvider,
        string tag)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(tag);

        var data = new SortTestData
        {
            Tag = tag,
            AllResources = []
        };

        var baseDate = DateTime.UtcNow.Date;

        var robinsonDate = baseDate.AddDays(-90);
        data.PatientRobinson = new PatientBuilder(schemaProvider)
            .WithTag(tag)
            .WithFamilyName("Robinson")
            .WithGivenName("John")
            .WithBirthDate(robinsonDate.Year, robinsonDate.Month, robinsonDate.Day)
            .WithGender(g => g.Male)
            .WithCity("Seattle")
            .WithState("Washington")
            .Build();

        var williamsDate = baseDate.AddDays(-60);
        data.PatientWilliams = new PatientBuilder(schemaProvider)
            .WithTag(tag)
            .WithFamilyName("Williams")
            .WithGivenName("Jane")
            .WithBirthDate(williamsDate.Year, williamsDate.Month, williamsDate.Day)
            .WithGender(g => g.Female)
            .WithCity("Boston")
            .WithState("Massachusetts")
            .Build();

        var williamasDate = baseDate.AddDays(-40);
        data.PatientWilliamas = new PatientBuilder(schemaProvider)
            .WithTag(tag)
            .WithFamilyName("Williamas")
            .WithGivenName("Alex")
            .WithBirthDate(williamasDate.Year, williamasDate.Month, williamasDate.Day)
            .WithGender(g => g.Other)
            .WithCity("Chicago")
            .WithState("Illinois")
            .Build();

        var jonesDate = baseDate.AddDays(-30);
        data.PatientJones = new PatientBuilder(schemaProvider)
            .WithTag(tag)
            .WithFamilyName("Jones")
            .WithGivenName("Bob")
            .WithBirthDate(jonesDate.Year, jonesDate.Month, jonesDate.Day)
            .WithGender(g => g.Male)
            .WithCity("Seattle")
            .WithState("Washington")
            .Build();

        data.AllResources.Add(data.PatientRobinson);
        data.AllResources.Add(data.PatientWilliams);
        data.AllResources.Add(data.PatientWilliamas);
        data.AllResources.Add(data.PatientJones);

        return data;
    }

    /// <summary>
    /// Creates test data for paginated sort tests (12 patients).
    /// Patients have birthdays spanning from 1940 to 1970 for multi-page testing.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="tag">A unique tag for test isolation.</param>
    /// <returns>PaginatedSortTestData containing all created patients.</returns>
    public static PaginatedSortTestData GetPaginatedSortTestData(
        this IFhirSchemaProvider schemaProvider,
        string tag)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(tag);

        var cities = new[]
        {
            ("Seattle", "Washington"),
            ("Boston", "Massachusetts"),
            ("Chicago", "Illinois"),
            ("Houston", "Texas"),
            ("Phoenix", "Arizona"),
            ("Denver", "Colorado"),
            ("Portland", "Oregon"),
            ("Austin", "Texas"),
            ("Atlanta", "Georgia"),
            ("Miami", "Florida"),
            ("Detroit", "Michigan"),
            ("Cleveland", "Ohio")
        };

        var data = new PaginatedSortTestData
        {
            Tag = tag,
            Patients = []
        };

        for (var i = 0; i < 12; i++)
        {
            var birthYear = 1940 + (i * 2);
            var city = cities[i];

            var patient = new PatientBuilder(schemaProvider)
                .WithTag(tag)
                .WithFamilyName($"TestFamily{i:D2}")
                .WithGivenName($"Given{i:D2}")
                .WithBirthDate(birthYear, 6, 15)
                .WithGender(i % 2 == 0 ? g => g.Male : g => g.Female)
                .WithCity(city.Item1)
                .WithState(city.Item2)
                .Build();

            data.Patients.Add(patient);
        }

        return data;
    }

    /// <summary>
    /// Creates test data for sort with missing values (12 patients, 2 without birthdates).
    /// Used to test sorting behavior when some resources lack the sort field.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="tag">A unique tag for test isolation.</param>
    /// <returns>MissingValueSortTestData containing patients with and without birthdates.</returns>
    public static MissingValueSortTestData GetMissingValueSortTestData(
        this IFhirSchemaProvider schemaProvider,
        string tag)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(tag);

        var data = new MissingValueSortTestData
        {
            Tag = tag,
            PatientsWithBirthdate = [],
            PatientsWithoutBirthdate = []
        };

        for (var i = 0; i < 10; i++)
        {
            var birthYear = 1950 + (i * 3);

            var patient = new PatientBuilder(schemaProvider)
                .WithTag(tag)
                .WithFamilyName($"WithBirthdate{i:D2}")
                .WithGivenName($"Given{i:D2}")
                .WithBirthDate(birthYear, 3, 10)
                .WithGender(i % 2 == 0 ? g => g.Male : g => g.Female)
                .Build();

            data.PatientsWithBirthdate.Add(patient);
        }

        for (var i = 0; i < 2; i++)
        {
            var patientBuilder = new PatientBuilder(schemaProvider)
                .WithTag(tag)
                .WithFamilyName($"NoBirthdate{i:D2}")
                .WithGivenName($"Given{i:D2}")
                .WithGender(g => g.Unknown);

            var patient = patientBuilder.Build();
            patient.MutableNode.Remove("birthDate");

            data.PatientsWithoutBirthdate.Add(patient);
        }

        return data;
    }

    /// <summary>
    /// Creates test data for sort with multiple names per patient.
    /// Used to test sorting when patients have multiple family names.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="tag">A unique tag for test isolation.</param>
    /// <returns>MultiNameSortTestData containing patients with multiple names.</returns>
    public static MultiNameSortTestData GetMultiNameSortTestData(
        this IFhirSchemaProvider schemaProvider,
        string tag)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(tag);

        var data = new MultiNameSortTestData
        {
            Tag = tag,
            Patients = []
        };

        var nameConfigs = new[]
        {
            new[] { "Rosen", "Adams" },
            new[] { "Roberts", "Baker" },
            new[] { "Reynard", "Clark" },
            new[] { "Russell", "Davis", "Edwards" },
            new[] { "Ramirez", "Foster", "Garcia" }
        };

        for (var i = 0; i < nameConfigs.Length; i++)
        {
            var builder = new PatientBuilder(schemaProvider)
                .WithTag(tag)
                .WithFamilyName(nameConfigs[i][0])
                .WithGivenName($"MultiName{i:D2}")
                .WithBirthDate(1970 + i, 1, 15)
                .WithGender(i % 2 == 0 ? g => g.Male : g => g.Female);

            for (var j = 1; j < nameConfigs[i].Length; j++)
            {
                builder.AddName(nameConfigs[i][j], $"Alt{j}");
            }

            var patient = builder.Build();

            data.Patients.Add(patient);
            data.FamilyNames.Add(nameConfigs[i].ToList());
        }

        return data;
    }

    /// <summary>
    /// Creates test data for sort with Organization chain.
    /// Patients have managing organizations for testing chained sort.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="tag">A unique tag for test isolation.</param>
    /// <returns>ChainedSortTestData containing organizations and linked patients.</returns>
    public static ChainedSortTestData GetChainedSortTestData(
        this IFhirSchemaProvider schemaProvider,
        string tag)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(tag);

        var data = new ChainedSortTestData
        {
            Tag = tag,
            Organizations = [],
            Patients = [],
            AllResources = []
        };

        var org1 = OrganizationBuilder.Create(schemaProvider)
            .WithTag(tag)
            .WithName("Alpha Hospital")
            .WithType("prov", display: "Healthcare Provider")
            .Build();

        var org2 = OrganizationBuilder.Create(schemaProvider)
            .WithTag(tag)
            .WithName("Beta Clinic")
            .WithType("prov", display: "Healthcare Provider")
            .Build();

        var org3 = OrganizationBuilder.Create(schemaProvider)
            .WithTag(tag)
            .WithName("Gamma Medical Center")
            .WithType("prov", display: "Healthcare Provider")
            .Build();

        data.Organizations.Add(org1);
        data.Organizations.Add(org2);
        data.Organizations.Add(org3);
        data.AllResources.AddRange(data.Organizations);

        var patient1 = new PatientBuilder(schemaProvider)
            .WithTag(tag)
            .WithFamilyName("OrgTest1")
            .WithGivenName("Alice")
            .WithManagingOrganization(org1.Id!)
            .WithBirthDate(1980, 1, 1)
            .Build();

        var patient2 = new PatientBuilder(schemaProvider)
            .WithTag(tag)
            .WithFamilyName("OrgTest2")
            .WithGivenName("Bob")
            .WithManagingOrganization(org2.Id!)
            .WithBirthDate(1985, 6, 15)
            .Build();

        var patient3 = new PatientBuilder(schemaProvider)
            .WithTag(tag)
            .WithFamilyName("OrgTest3")
            .WithGivenName("Charlie")
            .WithManagingOrganization(org3.Id!)
            .WithBirthDate(1990, 12, 25)
            .Build();

        data.Patients.Add(patient1);
        data.Patients.Add(patient2);
        data.Patients.Add(patient3);
        data.AllResources.AddRange(data.Patients);

        return data;
    }

    /// <summary>
    /// Creates test data for sort with Observation includes.
    /// Patients with observations for testing _revinclude with sort.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="tag">A unique tag for test isolation.</param>
    /// <returns>IncludeSortTestData containing patients and observations.</returns>
    public static IncludeSortTestData GetIncludeSortTestData(
        this IFhirSchemaProvider schemaProvider,
        string tag)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(tag);

        var data = new IncludeSortTestData
        {
            Tag = tag,
            Patients = [],
            Observations = [],
            AllResources = []
        };

        for (var i = 0; i < 5; i++)
        {
            var patient = new PatientBuilder(schemaProvider)
                .WithTag(tag)
                .WithFamilyName($"IncludePatient{i:D2}")
                .WithGivenName($"Given{i:D2}")
                .WithBirthDate(1960 + (i * 5), 6, 15)
                .WithGender(i % 2 == 0 ? g => g.Male : g => g.Female)
                .Build();

            data.Patients.Add(patient);
            data.AllResources.Add(patient);

            for (var j = 0; j < 2; j++)
            {
                var effectiveDate = new DateTime(2023, 1 + i, 10 + j);

                var observation = ObservationBuilder.Create(schemaProvider)
                    .WithTag(tag)
                    .WithCode("29463-7", "http://loinc.org", "Body Weight")
                    .WithSubject(patient.Id!)
                    .WithQuantityValue(70 + i + j, "kg")
                    .WithEffectiveDateTime(effectiveDate.ToString("yyyy-MM-dd"))
                    .WithStatus("final")
                    .Build();

                data.Observations.Add(observation);
                data.AllResources.Add(observation);
            }
        }

        return data;
    }

    /// <summary>
    /// Creates test data for patients with same birthdate (tie-breaking tests).
    /// Multiple patients share the same birthdate to test secondary sort behavior.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="tag">A unique tag for test isolation.</param>
    /// <returns>SameBirthdateSortTestData containing patients with identical birthdates.</returns>
    public static SameBirthdateSortTestData GetSameBirthdateSortTestData(
        this IFhirSchemaProvider schemaProvider,
        string tag)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(tag);

        var data = new SameBirthdateSortTestData
        {
            Tag = tag,
            Patients = []
        };

        for (var i = 0; i < 15; i++)
        {
            var patient = new PatientBuilder(schemaProvider)
                .WithTag(tag)
                .WithFamilyName($"SameBirthdate{i:D2}")
                .WithGivenName($"Given{i:D2}")
                .WithBirthDate(1980, 6, 15)
                .WithGender(i % 2 == 0 ? g => g.Male : g => g.Female)
                .Build();

            data.Patients.Add(patient);
        }

        return data;
    }

    /// <summary>
    /// Creates test data for patients with missing family names.
    /// Used to test sorting when some patients lack the family name field.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="tag">A unique tag for test isolation.</param>
    /// <returns>MissingFamilyNameSortTestData containing patients with and without family names.</returns>
    public static MissingFamilyNameSortTestData GetMissingFamilyNameSortTestData(
        this IFhirSchemaProvider schemaProvider,
        string tag)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(tag);

        var data = new MissingFamilyNameSortTestData
        {
            Tag = tag,
            PatientsWithFamilyName = [],
            PatientsWithoutFamilyName = []
        };

        var familyNames = new[] { "Adams", "Baker", "Clark", "Davis" };

        for (var i = 0; i < familyNames.Length; i++)
        {
            var patient = new PatientBuilder(schemaProvider)
                .WithTag(tag)
                .WithFamilyName(familyNames[i])
                .WithGivenName($"Given{i:D2}")
                .WithBirthDate(1970 + (i * 5), 3, 10)
                .Build();

            data.PatientsWithFamilyName.Add(patient);
        }

        for (var i = 0; i < 2; i++)
        {
            var patient = new PatientBuilder(schemaProvider)
                .WithTag(tag)
                .WithGivenName($"NoFamily{i:D2}")
                .WithBirthDate(1975 + i, 6, 20)
                .Build();

            var namesArray = patient.MutableNode["name"] as System.Text.Json.Nodes.JsonArray;
            if (namesArray is not null && namesArray.Count > 0)
            {
                var nameObj = namesArray[0] as System.Text.Json.Nodes.JsonObject;
                nameObj?.Remove("family");
            }

            data.PatientsWithoutFamilyName.Add(patient);
        }

        return data;
    }
}

/// <summary>
/// Contains test data for basic sort tests.
/// </summary>
public sealed class SortTestData
{
    public required string Tag { get; init; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public required List<ResourceJsonNode> AllResources { get; init; }

    public ResourceJsonNode PatientRobinson { get; set; } = null!;
    public ResourceJsonNode PatientWilliams { get; set; } = null!;
    public ResourceJsonNode PatientWilliamas { get; set; } = null!;
    public ResourceJsonNode PatientJones { get; set; } = null!;
}

/// <summary>
/// Contains test data for paginated sort tests.
/// </summary>
public sealed class PaginatedSortTestData
{
    public required string Tag { get; init; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public required List<ResourceJsonNode> Patients { get; init; }
}

/// <summary>
/// Contains test data for sort tests with missing values.
/// </summary>
public sealed class MissingValueSortTestData
{
    public required string Tag { get; init; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public required List<ResourceJsonNode> PatientsWithBirthdate { get; init; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public required List<ResourceJsonNode> PatientsWithoutBirthdate { get; init; }

    public IEnumerable<ResourceJsonNode> AllPatients => PatientsWithBirthdate.Concat(PatientsWithoutBirthdate);
}

/// <summary>
/// Contains test data for multi-name sort tests.
/// </summary>
public sealed class MultiNameSortTestData
{
    public required string Tag { get; init; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public required List<ResourceJsonNode> Patients { get; init; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<List<string>> FamilyNames { get; } = [];
}

/// <summary>
/// Contains test data for chained sort tests.
/// </summary>
public sealed class ChainedSortTestData
{
    public required string Tag { get; init; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public required List<ResourceJsonNode> Organizations { get; init; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public required List<ResourceJsonNode> Patients { get; init; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public required List<ResourceJsonNode> AllResources { get; init; }
}

/// <summary>
/// Contains test data for include sort tests.
/// </summary>
public sealed class IncludeSortTestData
{
    public required string Tag { get; init; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public required List<ResourceJsonNode> Patients { get; init; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public required List<ResourceJsonNode> Observations { get; init; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public required List<ResourceJsonNode> AllResources { get; init; }
}

/// <summary>
/// Contains test data for same birthdate sort tests.
/// </summary>
public sealed class SameBirthdateSortTestData
{
    public required string Tag { get; init; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public required List<ResourceJsonNode> Patients { get; init; }
}

/// <summary>
/// Contains test data for missing family name sort tests.
/// </summary>
public sealed class MissingFamilyNameSortTestData
{
    public required string Tag { get; init; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public required List<ResourceJsonNode> PatientsWithFamilyName { get; init; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public required List<ResourceJsonNode> PatientsWithoutFamilyName { get; init; }

    public IEnumerable<ResourceJsonNode> AllPatients => PatientsWithFamilyName.Concat(PatientsWithoutFamilyName);
}

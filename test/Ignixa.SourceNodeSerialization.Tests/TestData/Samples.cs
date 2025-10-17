// <copyright file="Samples.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

#pragma warning disable CA1861 // Prefer 'static readonly' fields over constant array arguments

using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

namespace Ignixa.SourceNodeSerialization.Tests.TestData;

/// <summary>
/// Provides sample FHIR resources for testing.
/// </summary>
public static class Samples
{
    /// <summary>
    /// Gets a default Patient resource for testing.
    /// </summary>
    public static Patient GetDefaultPatient()
    {
        return new Patient
        {
            Id = "example",
            Name = new List<HumanName>
            {
                new HumanName
                {
                    Use = HumanName.NameUse.Official,
                    Given = new[] { "Peter", "James" },
                    Family = "Chalmers",
                },
            },
            Gender = AdministrativeGender.Male,
            BirthDate = "1974-12-25",
            Active = true,
            Telecom = new List<ContactPoint>
            {
                new ContactPoint
                {
                    System = ContactPoint.ContactPointSystem.Phone,
                    Value = "(03) 5555 6473",
                    Use = ContactPoint.ContactPointUse.Work,
                },
            },
            Address = new List<Address>
            {
                new Address
                {
                    Use = Address.AddressUse.Home,
                    Line = new[] { "534 Erewhon St" },
                    City = "PleasantVille",
                    State = "Vic",
                    PostalCode = "3999",
                },
            },
        };
    }

    /// <summary>
    /// Gets a default Observation resource for testing.
    /// </summary>
    public static Observation GetDefaultObservation()
    {
        return new Observation
        {
            Id = "example",
            Status = ObservationStatus.Final,
            Code = new CodeableConcept
            {
                Coding = new List<Coding>
                {
                    new Coding
                    {
                        System = "http://loinc.org",
                        Code = "15074-8",
                        Display = "Glucose [Moles/volume] in Blood",
                    },
                },
            },
            Subject = new ResourceReference("Patient/example"),
            Effective = new FhirDateTime("2013-04-02T09:30:10+01:00"),
            Value = new Quantity
            {
                Value = 6.3m,
                Unit = "mmol/L",
                System = "http://unitsofmeasure.org",
                Code = "mmol/L",
            },
        };
    }

    /// <summary>
    /// Gets the JSON representation of a Patient resource.
    /// </summary>
    public static string GetJson(string resourceType)
    {
        return resourceType switch
        {
            "Patient" => GetDefaultPatient().ToJson(),
            "Observation" => GetDefaultObservation().ToJson(),
            _ => throw new ArgumentException($"Unknown resource type: {resourceType}", nameof(resourceType)),
        };
    }
}

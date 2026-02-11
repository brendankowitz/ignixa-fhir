// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.Anonymizer.Configuration;

namespace Ignixa.Anonymizer;

internal static class Constants
{
    internal static readonly HashSet<string> SupportedFhirVersions = new(StringComparer.OrdinalIgnoreCase)
    {
        "R4", "R4B", "R5", "R6", "STU3"
    };

    internal static FhirVersion ParseFhirVersion(string? version)
    {
        return version?.ToUpperInvariant() switch
        {
            "R4" => FhirVersion.R4,
            "R4B" => FhirVersion.R4B,
            "R5" => FhirVersion.R5,
            "R6" => FhirVersion.R6,
            "STU3" => FhirVersion.Stu3,
            _ => FhirVersion.R4  // Default to R4 for backward compatibility
        };
    }

    // InstanceType constants
    internal const string DateTypeName = "date";
    internal const string DateTimeTypeName = "dateTime";
    internal const string DecimalTypeName = "decimal";
    internal const string InstantTypeName = "instant";
    internal const string AgeTypeName = "Age";
    internal const string BundleTypeName = "Bundle";
    internal const string ReferenceTypeName = "Reference";

    // FHIR primitive numeric type names (replaces FHIRAllTypes enum references)
    internal const string DecimalFhirTypeName = "decimal";
    internal const string IntegerFhirTypeName = "integer";
    internal const string PositiveIntFhirTypeName = "positiveInt";
    internal const string UnsignedIntFhirTypeName = "unsignedInt";

    // Quantity-like type names
    internal const string QuantityTypeName = "Quantity";
    internal const string SimpleQuantityTypeName = "SimpleQuantity";
    internal const string MoneyTypeName = "Money";

    // NodeName constants
    internal const string PostalCodeNodeName = "postalCode";
    internal const string ReferenceStringNodeName = "reference";
    internal const string ContainedNodeName = "contained";
    internal const string EntryNodeName = "entry";
    internal const string EntryResourceNodeName = "resource";
    internal const string ValueNodeName = "value";

    // Rule constants
    internal const string PathKey = "path";
    internal const string MethodKey = "method";

    internal const string GeneralResourceType = "Resource";
    internal const string GeneralDomainResourceType = "DomainResource";

    internal static readonly HashSet<string> BuiltInMethods = Enum.GetNames(typeof(AnonymizerMethod)).ToHashSet(StringComparer.InvariantCultureIgnoreCase);
}

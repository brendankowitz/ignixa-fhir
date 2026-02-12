// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.Anonymizer.Configuration;

namespace Ignixa.Anonymizer;

/// <summary>
/// Internal constants for FHIR type names, node names, and rule configuration keys.
/// </summary>
internal static class Constants
{
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
}

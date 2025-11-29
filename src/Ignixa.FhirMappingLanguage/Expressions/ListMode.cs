/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// List mode for target elements.
/// </summary>
public enum ListMode
{
    First,
    NotFirst,
    Last,
    NotLast,
    OnlyOne,
    Share,
#pragma warning disable CA1720 // Identifier contains type name - 'Single' is a FHIR spec keyword for list modes
    Single
#pragma warning restore CA1720
}

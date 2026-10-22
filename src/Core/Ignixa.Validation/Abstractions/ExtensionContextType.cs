// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions;

/// <summary>The kind of an extension definition's context (<c>StructureDefinition.context.type</c>).</summary>
public enum ExtensionContextType
{
    /// <summary>An element: a type name (<c>Patient</c>, <c>HumanName</c>, <c>Element</c>) or an element path (<c>Patient.contact</c>).</summary>
    Element,

    /// <summary>Another extension, by its url: the extension is one of that extension's sub-extensions.</summary>
    Extension,

    /// <summary>A FHIRPath expression selecting where the extension may appear.</summary>
    FhirPath,
}

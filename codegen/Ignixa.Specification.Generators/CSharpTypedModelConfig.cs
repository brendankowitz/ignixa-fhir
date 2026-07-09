// <copyright file="CSharpTypedModelConfig.cs" company="Ignixa Contributors">
//     Copyright (c) Ignixa Contributors. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

namespace Ignixa.Specification.Generators;

/// <summary>
/// Configuration for the C# typed-model (POCO facade) generator.
/// </summary>
public sealed class CSharpTypedModelConfig
{
    /// <summary>Gets or sets the output directory for generated facade files.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the namespace for generated facade classes.</summary>
    public string Namespace { get; set; } = "Ignixa.Models.R4";

    /// <summary>Gets or sets the resources to generate facades for (MVP allow-list).</summary>
    public IReadOnlyCollection<string> ResourceAllowList { get; set; } = [];

    /// <summary>Gets or sets the complex datatypes to generate facades for (MVP allow-list).</summary>
    /// <remarks>Ignored when <see cref="GenerateAllDatatypes"/> is <c>true</c>.</remarks>
    public IReadOnlyCollection<string> DatatypeAllowList { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether to generate facades for the FULL set of concrete
    /// FHIR complex datatypes for the version (every entry in <c>ComplexTypesByName</c> that is not
    /// an abstract base), rather than the hand-picked <see cref="DatatypeAllowList"/>. Generating the
    /// full closure resolves Extension, Identifier, etc. to real facades and eliminates the JsonNode
    /// fallback for most in-spec complex types. This does NOT change how <c>Reference</c>-typed
    /// elements are emitted: <c>Reference</c> itself still gets a base facade (it's a normal concrete
    /// datatype, needed as e.g. an <c>Identifier</c>'s target), but elements whose type IS Reference
    /// (<c>Patient.generalPractitioner</c>, etc.) always fall back to a raw JsonNode regardless of this
    /// flag -- see <c>AbstractOrFallbackTypes</c> in <c>CSharpTypedModelLanguage</c>. Defaults to
    /// <c>false</c>; the <c>typed-model</c> mode sets it to <c>true</c>.
    /// </summary>
    public bool GenerateAllDatatypes { get; set; }
}

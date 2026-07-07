// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Services;

namespace Ignixa.PackageManagement.Validation;

/// <summary>
/// The composed, ready-to-use result of <see cref="PackageBackedValidator.Create"/>: a
/// profile-aware schema resolver plus the terminology and content providers it was wired with.
/// Callers validate through <see cref="SchemaResolver"/> (via <c>ResolveForElement</c> for
/// <c>meta.profile</c> resolution, or <c>GetSchema</c> for an explicit canonical) and can inspect
/// the providers to confirm resolution.
/// </summary>
public sealed class PackageBackedValidationSetup
{
    internal PackageBackedValidationSetup(
        ProfileAwareValidationSchemaResolver schemaResolver,
        ITerminologyService terminologyService,
        IFhirSchemaProvider schemaProvider,
        IValueSetProvider valueSetProvider,
        ICodeSystemProvider codeSystemProvider)
    {
        SchemaResolver = schemaResolver;
        TerminologyService = terminologyService;
        SchemaProvider = schemaProvider;
        ValueSetProvider = valueSetProvider;
        CodeSystemProvider = codeSystemProvider;
    }

    /// <summary>
    /// Profile-aware resolver layering the packages' profiles/extensions over the base schema.
    /// Implements both <see cref="IValidationSchemaResolver"/> and
    /// <see cref="IElementSchemaResolver"/>.
    /// </summary>
    public ProfileAwareValidationSchemaResolver SchemaResolver { get; }

    /// <summary>
    /// Terminology service backed by the base value sets, the packages' value sets (when layered),
    /// and the packages' CodeSystem content for <c>$lookup</c>.
    /// </summary>
    public ITerminologyService TerminologyService { get; }

    /// <summary>
    /// Schema provider that resolves profile and extension <c>StructureDefinition</c>s by id,
    /// falling back to the base schema.
    /// </summary>
    public IFhirSchemaProvider SchemaProvider { get; }

    /// <summary>
    /// The value-set surface (base plus, when layered, the packages' value sets).
    /// </summary>
    public IValueSetProvider ValueSetProvider { get; }

    /// <summary>
    /// The packages' CodeSystem content surface (code&#8594;display and membership).
    /// </summary>
    public ICodeSystemProvider CodeSystemProvider { get; }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Microsoft.Extensions.Logging;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates every <c>extension</c> and <c>modifierExtension</c> of a resource, wherever it appears: on the resource, a
/// backbone element, a datatype (including a CodeableConcept, Coding or Reference, which have no nested schema of their own),
/// a choice variant, a primitive's <c>_field</c> shadow, or another extension. Contained and Bundle/Parameters entry
/// resources are left to their own validation. Spec tier, once per resource.
/// </summary>
/// <remarks>
/// <para>
/// At Spec and Compatibility depth each extension is validated as the Extension datatype: <c>url</c> required, a single
/// <c>value[x]</c> of an allowed type whose own subtree is validated like any complex choice variant, no unknown properties.
/// </para>
/// <para>
/// At Full depth an extension whose <c>url</c> names a loaded extension definition is validated against that definition
/// instead, which narrows the value's types, carries its bindings and cardinalities, and slices a complex extension's
/// sub-extensions. An extension without a loaded definition is validated as the datatype and reported as a non-failing
/// <c>extension-definition-unchecked</c> information issue (the HL7 validator's "unknown extension" hint), never passed as
/// checked. So is a sub-extension of a loaded complex extension that the definition identifies by a relative url: the slice
/// rules a definition gives such sub-extensions are not modelled. An extension's context (where a definition allows it) is
/// not checked at any depth.
/// </para>
/// <para>
/// Definitions are looked up in the schema the resource schema was built from, so a host that layers packages over the
/// core schema gets their extensions validated. Built definition schemas are cached per check instance; lookups that find
/// no definition are not cached, so the cache is bounded by the definitions loaded. Thread-safe.
/// </para>
/// </remarks>
internal sealed class ExtensionCheck : IValidationCheck, ISingletonCheck
{
    private const string BaseExtensionUrl = "http://hl7.org/fhir/StructureDefinition/Extension";

    private readonly ISchema _schema;
    private readonly ITerminologyService? _terminologyService;
    private readonly FhirPathParser _parser;
    private readonly ILogger<StructureDefinitionSchemaBuilder>? _logger;
    private readonly Func<string, bool> _isResourceType;
    private readonly Lazy<ValidationSchema> _datatype;
    private readonly ConcurrentDictionary<string, Lazy<ValidationSchema>> _definitions = new(StringComparer.Ordinal);

    /// <param name="extensionType">The Extension datatype's definition.</param>
    /// <param name="schema">The schema to build from and look extension definitions up in.</param>
    /// <param name="terminologyService">The terminology service binding checks use; null for none.</param>
    /// <param name="parser">The FHIRPath parser invariant checks use.</param>
    /// <param name="logger">Logger for schema building.</param>
    /// <param name="isResourceType">Whether a name is a resource type of the FHIR version.</param>
    public ExtensionCheck(IType extensionType, ISchema schema, ITerminologyService? terminologyService, FhirPathParser parser, ILogger<StructureDefinitionSchemaBuilder>? logger, Func<string, bool> isResourceType)
    {
        ArgumentNullException.ThrowIfNull(extensionType);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(isResourceType);

        _schema = schema;
        _terminologyService = terminologyService;
        _parser = parser;
        _logger = logger;
        _isResourceType = isResourceType;
        _datatype = new Lazy<ValidationSchema>(() => Build(extensionType, canonicalUrl: null));
    }

    /// <inheritdoc />
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        if (settings.Depth == ValidationDepth.Minimal)
        {
            return ValidationResult.Success();
        }

        var results = new List<ValidationResult>();
        Walk(element, settings, state, inDefinedExtension: false, results);
        return ValidationResult.Combine(results);
    }

    /// <summary>Visits every descendant of <paramref name="node"/> outside nested resources, validating the extensions.</summary>
    /// <param name="inDefinedExtension">Whether <paramref name="node"/> is an extension validated against a loaded definition.</param>
    private void Walk(IElement node, ValidationSettings settings, ValidationState state, bool inDefinedExtension, List<ValidationResult> results)
    {
        foreach (var child in node.Children())
        {
            if (child.Name == "contained" || (child.InstanceType is { } type && _isResourceType(type)))
            {
                continue;
            }

            if (child.Name is not ("extension" or "modifierExtension"))
            {
                Walk(child, settings, state, inDefinedExtension: false, results);
                continue;
            }

            if (!state.TryDescend(out var descended))
            {
                results.Add(new ValidationResult(true, [ValidationIssue.NestingLimitExceeded(state.Location.InstancePath, child.Name)]));
                continue;
            }

            var nested = descended.WithLocation(child.Location);
            bool defined = ValidateExtension(child, settings, nested, inDefinedExtension, results);
            Walk(child, settings, nested, defined, results);
        }
    }

    /// <summary>Validates one extension; true when it was validated against a loaded definition.</summary>
    private bool ValidateExtension(IElement extension, ValidationSettings settings, ValidationState state, bool isSubExtension, List<ValidationResult> results)
    {
        string? url = extension.Children("url") is [{ Value: { } value }, ..] ? value.ToString() : null;
        if (settings.Depth != ValidationDepth.Full)
        {
            results.Add(_datatype.Value.Validate(extension, settings, state));
            return false;
        }

        bool absolute = Uri.TryCreate(url, UriKind.Absolute, out _);
        if (absolute && url is { } absoluteUrl && Definition(absoluteUrl) is { } definition)
        {
            results.Add(definition.Validate(extension, settings, state));
            return true;
        }

        results.Add(_datatype.Value.Validate(extension, settings, state));
        if (url is { } named)
        {
            // A url-less extension is an error the datatype's cardinality already reports.
            var template = isSubExtension && !absolute ? IssueTemplates.ExtensionSliceNotValidated : IssueTemplates.ExtensionDefinitionNotLoaded;
            results.Add(new ValidationResult(true, [ValidationIssue.FromTemplate(IssueSeverity.Information, "extension-definition-unchecked", extension.Location, template, named)]));
        }

        return false;
    }

    /// <summary>The loaded extension definition <paramref name="url"/> names, built into a schema; null when none is loaded.</summary>
    private ValidationSchema? Definition(string url)
    {
        if (_definitions.TryGetValue(url, out var cached))
        {
            return cached.Value;
        }

        if (url == BaseExtensionUrl || _schema.GetTypeDefinition(url) is not { Info.Name: "Extension" } definition)
        {
            return null;
        }

        return _definitions.GetOrAdd(url, _ => new Lazy<ValidationSchema>(() => Build(definition, url))).Value;
    }

    private ValidationSchema Build(IType definition, string? canonicalUrl) =>
        new StructureDefinitionSchemaBuilder(_parser, _logger)
            .BuildSchema(definition, _schema, _terminologyService, validResourceTypes: null, validationSchemaResolver: null, canonicalUrl);
}

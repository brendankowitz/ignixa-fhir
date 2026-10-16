// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Microsoft.Extensions.Logging;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates every <c>extension</c> and <c>modifierExtension</c> of a resource, wherever it appears: on the resource, a
/// backbone element, a datatype, a choice variant, a primitive's <c>_field</c> shadow, or another extension. Contained and
/// Bundle/Parameters entry resources are left to their own validation. Spec tier, once per resource.
/// </summary>
/// <remarks>
/// <para>
/// At Spec and Compatibility depth each extension is validated as the Extension datatype: <c>url</c> required, a single
/// <c>value[x]</c> of an allowed type whose own subtree is validated like any complex choice variant, no unknown properties.
/// At Spec and Full an extension that is not a sub-extension of another must have an absolute url
/// (<c>extension-url-relative</c>); only a complex extension names its sub-extensions relatively.
/// </para>
/// <para>
/// At Full depth an extension whose <c>url</c> names a loaded extension definition is validated against that definition
/// instead, which narrows the value's types, carries its bindings and cardinalities, and slices a complex extension's
/// sub-extensions; and its use is checked against the definition (<see cref="IExtensionDefinitionInfo"/>, from the schema):
/// a modifier extension only under <c>modifierExtension</c> and the others never there (<c>extension-modifier-mismatch</c>),
/// and only on an element one of its contexts allows (<c>extension-context</c>: an element context names the element's type,
/// its element path, its path within its datatype, or <c>Element</c>; an extension context names the url of the extension
/// it is nested in). A FHIRPath context, or a schema that does not describe contexts, is reported as a non-failing
/// <c>extension-context-unchecked</c> issue. An extension without a loaded definition is validated as the datatype and
/// reported as a non-failing <c>extension-definition-unchecked</c> information issue (the HL7 validator's "unknown
/// extension" hint), never passed as checked. So is a sub-extension of a loaded complex extension that the definition
/// identifies by a relative url: the slice rules a definition gives such sub-extensions are not modelled.
/// </para>
/// <para>
/// Definitions are looked up in the schema the resource schema was built from, so a host that layers packages over the
/// core schema gets their extensions validated. Built definition schemas are cached per check instance; lookups that find
/// no definition are not cached, so the cache is bounded by the definitions loaded. A resource whose JSON has no extension
/// is not walked. Thread-safe.
/// </para>
/// </remarks>
internal sealed class ExtensionCheck : IValidationCheck, ISingletonCheck
{
    private const string BaseExtensionUrl = "http://hl7.org/fhir/StructureDefinition/Extension";

    private readonly ISchema _schema;
    private readonly ITerminologyService? _terminologyService;
    private readonly FhirPathParser _parser;
    private readonly ILogger<StructureDefinitionSchemaBuilder>? _logger;
    private readonly Lazy<ValidationSchema> _datatype;
    private readonly ConcurrentDictionary<string, Lazy<ValidationSchema>> _definitions = new(StringComparer.Ordinal);

    /// <param name="extensionType">The Extension datatype's definition.</param>
    /// <param name="schema">The schema to build from and look extension definitions up in.</param>
    /// <param name="terminologyService">The terminology service binding checks use; null for none.</param>
    /// <param name="parser">The FHIRPath parser invariant checks use.</param>
    /// <param name="logger">Logger for schema building.</param>
    public ExtensionCheck(IType extensionType, ISchema schema, ITerminologyService? terminologyService, FhirPathParser parser, ILogger<StructureDefinitionSchemaBuilder>? logger)
    {
        ArgumentNullException.ThrowIfNull(extensionType);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(parser);

        _schema = schema;
        _terminologyService = terminologyService;
        _parser = parser;
        _logger = logger;
        _datatype = new Lazy<ValidationSchema>(() => Build(extensionType, canonicalUrl: null));
    }

    /// <inheritdoc />
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        if (settings.Depth == ValidationDepth.Minimal || (element.Meta<JsonNode>() is JsonObject raw && !HasExtension(raw)))
        {
            return ValidationResult.Success();
        }

        var results = new List<ValidationResult>();
        Walk(element, ExtensionHost.ForResource(element), settings, state, results);
        return ValidationResult.Combine(results);
    }

    /// <summary>
    /// Whether the resource's own JSON (contained and entry resources aside, which are validated on their own) has an
    /// extension anywhere: a cheap test that spares the element walk for the many resources without one.
    /// </summary>
    private static bool HasExtension(JsonObject node)
    {
        foreach (var (key, value) in node)
        {
            if (key is "extension" or "modifierExtension")
            {
                return true;
            }

            if (key == "contained")
            {
                continue;
            }

            switch (value)
            {
                case JsonObject child when !child.ContainsKey("resourceType") && HasExtension(child):
                    return true;
                case JsonArray array when array.OfType<JsonObject>().Any(item => !item.ContainsKey("resourceType") && HasExtension(item)):
                    return true;
            }
        }

        return false;
    }

    /// <summary>Visits every descendant of <paramref name="node"/> outside nested resources, validating the extensions.</summary>
    private void Walk(IElement node, ExtensionHost host, ValidationSettings settings, ValidationState state, List<ValidationResult> results)
    {
        foreach (var child in node.Children())
        {
            if (child.Name == "contained" || EntryResourceCheck.IsEntryResource(child))
            {
                continue;
            }

            if (child.Name is not ("extension" or "modifierExtension"))
            {
                Walk(child, host.Child(child), settings, state, results);
                continue;
            }

            if (!state.TryDescend(out var descended))
            {
                results.Add(new ValidationResult(true, [ValidationIssue.NestingLimitExceeded(state.Location.InstancePath, child.Name)]));
                continue;
            }

            var nested = descended.WithLocation(child.Location);
            string? url = child.Children("url") is [{ Value: { } value }, ..] ? value.ToString() : null;
            bool defined = ValidateExtension(child, url, host, settings, nested, results);
            Walk(child, host.Extension(child, url, defined), settings, nested, results);
        }
    }

    /// <summary>Validates one extension used on <paramref name="host"/>; true when it was validated against a loaded definition.</summary>
    private bool ValidateExtension(IElement extension, string? url, ExtensionHost host, ValidationSettings settings, ValidationState state, List<ValidationResult> results)
    {
        bool absolute = Uri.TryCreate(url, UriKind.Absolute, out _);
        if (url is { Length: > 0 } relative && !absolute && !host.IsExtension && settings.Depth != ValidationDepth.Compatibility)
        {
            // Only a complex extension's sub-extensions may be named relatively (R4 extensibility).
            results.Add(ValidationResult.Failure(ValidationIssue.FromTemplate(IssueSeverity.Error, "extension-url-relative", extension.Location, IssueTemplates.ExtensionUrlRelative, relative)));
        }

        if (settings.Depth != ValidationDepth.Full)
        {
            results.Add(_datatype.Value.Validate(extension, settings, state));
            return false;
        }

        if (absolute && url is { } absoluteUrl && Definition(absoluteUrl) is { } definition)
        {
            results.Add(definition.Validate(extension, settings, state));
            results.Add(ValidateUse(extension, absoluteUrl, host));
            return true;
        }

        results.Add(_datatype.Value.Validate(extension, settings, state));
        if (url is { } named && (absolute || host.IsExtension))
        {
            // A url-less extension is an error the datatype's cardinality already reports, a relative top-level one is
            // reported above.
            var template = host.InDefinedExtension && !absolute ? IssueTemplates.ExtensionSliceNotValidated : IssueTemplates.ExtensionDefinitionNotLoaded;
            results.Add(new ValidationResult(true, [ValidationIssue.FromTemplate(IssueSeverity.Information, "extension-definition-unchecked", extension.Location, template, named)]));
        }

        return false;
    }

    /// <summary>
    /// Where the extension is used, against its definition: under <c>modifierExtension</c> exactly when it is a modifier
    /// extension, and on an element one of its contexts allows. A FHIRPath context, or a schema that does not describe
    /// contexts, leaves the context reported as not checked.
    /// </summary>
    private ValidationResult ValidateUse(IElement extension, string url, ExtensionHost host)
    {
        if (_schema is not IExtensionDefinitionInfo described || described.GetExtensionDefinition(url) is not { } info)
        {
            return new ValidationResult(true, [ValidationIssue.FromTemplate(IssueSeverity.Information, "extension-context-unchecked", extension.Location, IssueTemplates.ExtensionContextNotDescribed, url)]);
        }

        var issues = new List<ValidationIssue>();
        bool asModifier = extension.Name == "modifierExtension";
        if (info.IsModifier != asModifier)
        {
            issues.Add(ValidationIssue.FromTemplate(
                IssueSeverity.Error,
                "extension-modifier-mismatch",
                extension.Location,
                info.IsModifier ? IssueTemplates.ExtensionModifierNotInModifierExtension : IssueTemplates.ExtensionNotModifierInModifierExtension,
                url));
        }

        if (info.Contexts.Count > 0 && !info.Contexts.Any(host.Allows))
        {
            issues.Add(info.Contexts.Any(context => context.Type == ExtensionContextType.FhirPath)
                ? ValidationIssue.FromTemplate(IssueSeverity.Information, "extension-context-unchecked", extension.Location, IssueTemplates.ExtensionContextFhirPathUnchecked, url)
                : ValidationIssue.FromTemplate(
                    IssueSeverity.Error,
                    "extension-context",
                    extension.Location,
                    IssueTemplates.ExtensionContextMismatch,
                    url,
                    string.Join(", ", info.Contexts.Select(context => context.Expression))));
        }

        return new ValidationResult(!issues.Any(issue => issue.Severity == IssueSeverity.Error), issues);
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

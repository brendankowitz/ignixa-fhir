// <copyright file="CSharpNarrativeTemplateLanguage.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.CodeGen.Language;
using Microsoft.Health.Fhir.CodeGen.Models;
using Microsoft.Health.Fhir.CodeGen.FhirExtensions;
using Microsoft.Health.Fhir.CodeGenCommon.Packaging;

namespace Ignixa.Specification.Generators;

/// <summary>
/// Custom ILanguage implementation for generating Scriban narrative templates for FHIR resources.
/// Generates template scaffolds for rendering human-readable narratives from FHIR resources.
/// </summary>
public sealed class CSharpNarrativeTemplateLanguage : ILanguage
{
    private const string LanguageName = "CSharpNarrativeTemplate";

    /// <summary>Gets the language name.</summary>
    public string Name => LanguageName;

    /// <summary>Gets the configuration type.</summary>
    public Type ConfigType => typeof(CSharpNarrativeTemplateConfig);

    /// <summary>Gets the FHIR primitive type map (not used for this generator).</summary>
    public Dictionary<string, string> FhirPrimitiveTypeMap => new();

    /// <summary>Gets a value indicating whether this language is idempotent.</summary>
    public bool IsIdempotent => true;

    /// <summary>
    /// Well-known resources that are considered Normative across FHIR versions.
    /// Resources not in this list are considered Trial-Use and placed in version-specific folders.
    /// </summary>
    private static readonly HashSet<string> NormativeResources = new()
    {
        // Foundation
        "CapabilityStatement",
        "OperationDefinition",
        "SearchParameter",
        "CompartmentDefinition",
        "StructureDefinition",
        "OperationOutcome",
        "Bundle",
        "Binary",

        // Patient Administration
        "Patient",
        "Practitioner",
        "PractitionerRole",
        "RelatedPerson",
        "Organization",
        "Location",
        "HealthcareService",

        // Clinical
        "Observation",
        "Condition",
        "Procedure",
        "AllergyIntolerance",
        "CarePlan",
        "MedicationRequest",
        "MedicationStatement",
        "Immunization",

        // Diagnostic
        "DiagnosticReport",

        // Documents
        "Composition",
        "DocumentReference",
    };

    /// <summary>
    /// Common elements that most resources have and should be displayed in narratives.
    /// Maps element name to a human-readable label key.
    /// </summary>
    private static readonly Dictionary<string, string> CommonElements = new()
    {
        ["id"] = "Id",
        ["identifier"] = "Identifier",
        ["status"] = "Status",
        ["name"] = "Name",
        ["code"] = "Code",
        ["subject"] = "Subject",
        ["patient"] = "Patient",
        ["encounter"] = "Encounter",
        ["performer"] = "Performer",
        ["author"] = "Author",
        ["date"] = "Date",
        ["dateTime"] = "DateTime",
        ["issued"] = "Issued",
        ["effective"] = "Effective",
        ["effectiveDateTime"] = "EffectiveDateTime",
        ["effectivePeriod"] = "EffectivePeriod",
        ["recorded"] = "Recorded",
        ["onset"] = "Onset",
        ["abatement"] = "Abatement",
        ["clinicalStatus"] = "ClinicalStatus",
        ["verificationStatus"] = "VerificationStatus",
        ["category"] = "Category",
        ["severity"] = "Severity",
        ["bodySite"] = "BodySite",
        ["value"] = "Value",
        ["valueQuantity"] = "Value",
        ["valueCodeableConcept"] = "Value",
        ["valueString"] = "Value",
        ["interpretation"] = "Interpretation",
        ["referenceRange"] = "ReferenceRange",
        ["text"] = "Text",
        ["description"] = "Description",
        ["note"] = "Notes",
    };

    /// <summary>Exports the narrative templates.</summary>
    /// <param name="config">The configuration.</param>
    /// <param name="definitions">The definitions to export.</param>
    public void Export(object config, DefinitionCollection definitions)
    {
        if (config is not CSharpNarrativeTemplateConfig templateConfig)
        {
            throw new ArgumentException($"Configuration must be of type {nameof(CSharpNarrativeTemplateConfig)}", nameof(config));
        }

        // Get the FHIR version
        string fhirVersion = definitions.FhirSequence switch
        {
            FhirReleases.FhirSequenceCodes.R4 => "R4",
            FhirReleases.FhirSequenceCodes.R4B => "R4B",
            FhirReleases.FhirSequenceCodes.R5 => "R5",
            FhirReleases.FhirSequenceCodes.R6 => "R6",
            FhirReleases.FhirSequenceCodes.STU3 => "STU3",
            _ => throw new ArgumentException($"Unsupported FHIR version: {definitions.FhirSequence}")
        };

        // Create output directories
        string outputDir = Path.GetFullPath(templateConfig.OutputDirectory);
        string normativeDir = Path.Combine(outputDir, "Templates", "Normative");
        string versionDir = Path.Combine(outputDir, "Templates", fhirVersion);
        string resourcesDir = Path.Combine(outputDir, "Resources");

        Directory.CreateDirectory(normativeDir);
        Directory.CreateDirectory(versionDir);
        Directory.CreateDirectory(resourcesDir);

        Console.WriteLine($"Generating Scriban narrative templates for FHIR {fhirVersion}...");
        Console.WriteLine($"Output directory: {outputDir}");
        Console.WriteLine();

        int normativeCount = 0;
        int trialUseCount = 0;
        var resxEntries = new Dictionary<string, string>();

        // Process each resource type
        foreach (var (resourceName, sd) in definitions.ResourcesByName.OrderBy(kvp => kvp.Key))
        {
            // Skip abstract resources
            if (sd.Abstract == true)
            {
                continue;
            }

            // Determine if normative based on maturity or known list
            bool isNormative = IsNormativeResource(resourceName, sd);
            string targetDir = isNormative ? normativeDir : versionDir;

            // Generate the template
            string template = GenerateResourceTemplate(resourceName, sd, definitions, resxEntries);

            // Write template file
            string templatePath = Path.Combine(targetDir, $"{resourceName}.scriban");
            File.WriteAllText(templatePath, template, Encoding.UTF8);

            if (isNormative)
            {
                normativeCount++;
            }
            else
            {
                trialUseCount++;
            }
        }

        // Generate resx file with localization entries
        string resxPath = Path.Combine(resourcesDir, $"NarrativeTemplates.{fhirVersion}.resx");
        GenerateResxFile(resxPath, resxEntries);

        // Generate a template loader helper
        string loaderPath = Path.Combine(outputDir, $"{fhirVersion}NarrativeTemplateLoader.g.cs");
        GenerateTemplateLoader(loaderPath, fhirVersion, definitions, templateConfig.Namespace);

        Console.WriteLine();
        Console.WriteLine($"Generated {normativeCount} normative templates in Templates/Normative/");
        Console.WriteLine($"Generated {trialUseCount} trial-use templates in Templates/{fhirVersion}/");
        Console.WriteLine($"Generated localization file: {resxPath}");
        Console.WriteLine($"Generated template loader: {loaderPath}");
    }

    /// <summary>
    /// Determines if a resource is normative based on maturity level or known list.
    /// </summary>
    private bool IsNormativeResource(string resourceName, StructureDefinition sd)
    {
        // Check known normative resources
        if (NormativeResources.Contains(resourceName))
        {
            return true;
        }

        // Check standards status extension
        string standardStatus = sd.cgStandardStatus();
        if (!string.IsNullOrEmpty(standardStatus) &&
            standardStatus.Equals("normative", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check FMM level (normative typically >= 5)
        int? maturityLevel = sd.cgMaturityLevel();
        if (maturityLevel >= 5)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Generates a Scriban template for a FHIR resource type.
    /// </summary>
    private string GenerateResourceTemplate(
        string resourceName,
        StructureDefinition sd,
        DefinitionCollection definitions,
        Dictionary<string, string> resxEntries)
    {
        var sb = new StringBuilder();

        // Template header
        sb.AppendLine($"{{{{~ # Auto-generated Scriban narrative template for {resourceName} ~}}}}");
        sb.AppendLine($"{{{{~ # FHIR StructureDefinition: {sd.Url} ~}}}}");
        sb.AppendLine();

        // Start the main container with WCAG-compliant structure
        sb.AppendLine($"<div class=\"fhir-narrative fhir-{resourceName.ToLowerInvariant()}\" lang=\"{{{{ l10n.lang }}}}\">");

        // Resource title/header section
        sb.AppendLine($"  <header class=\"fhir-header\">");
        sb.AppendLine($"    <h3 class=\"fhir-resource-type\">{{{{ l10n.t \"{resourceName}.Title\" }}}}</h3>");

        // Add resource ID if available
        sb.AppendLine($"    {{{{~ if (fhir.exists resource \"id\") ~}}}}");
        sb.AppendLine($"    <span class=\"fhir-id\" aria-label=\"{{{{ l10n.t \"{resourceName}.Id\" }}}}\">{{{{ fhir.path resource \"id\" }}}}</span>");
        sb.AppendLine($"    {{{{~ end ~}}}}");
        sb.AppendLine($"  </header>");
        sb.AppendLine();

        // Add resx entries for header
        resxEntries[$"{resourceName}.Title"] = resourceName;
        resxEntries[$"{resourceName}.Id"] = "Identifier";

        // Get top-level elements for this resource
        var elements = sd.cgElements(topLevelOnly: true, includeRoot: false, skipSlices: true).ToList();

        // Generate sections for key elements
        var renderedElements = new HashSet<string>();
        sb.AppendLine($"  <dl class=\"fhir-details\">");

        // First, render common elements in a predictable order
        foreach (var (elementName, labelKey) in CommonElements)
        {
            var element = elements.FirstOrDefault(e => e.cgName().Equals(elementName, StringComparison.OrdinalIgnoreCase));
            if (element != null && !renderedElements.Contains(elementName))
            {
                GenerateElementSection(sb, resourceName, element, resxEntries);
                renderedElements.Add(elementName);
            }
        }

        // Then render remaining significant elements
        foreach (var element in elements)
        {
            string elementName = element.cgName();

            // Skip already rendered, extension, and meta elements
            if (renderedElements.Contains(elementName) ||
                elementName == "extension" ||
                elementName == "modifierExtension" ||
                elementName == "meta" ||
                elementName == "implicitRules" ||
                elementName == "language" ||
                elementName == "contained" ||
                elementName == "text")
            {
                continue;
            }

            // Skip elements that are typically not human-readable
            if (element.Type.Any(t => t.Code == "Resource" || t.Code == "Extension"))
            {
                continue;
            }

            // Only render elements that are in summary or required
            if ((element.IsSummary ?? false) || element.cgCardinalityMin() > 0)
            {
                GenerateElementSection(sb, resourceName, element, resxEntries);
                renderedElements.Add(elementName);
            }
        }

        sb.AppendLine($"  </dl>");
        sb.AppendLine();

        // Add notes section if resource has a notes element
        var notesElement = elements.FirstOrDefault(e => e.cgName() == "note");
        if (notesElement != null)
        {
            sb.AppendLine($"  {{{{~ if (fhir.exists resource \"note\") ~}}}}");
            sb.AppendLine($"  <section class=\"fhir-notes\" aria-labelledby=\"notes-heading-{{{{ fhir.path resource \"id\" }}}}\">");
            sb.AppendLine($"    <h4 id=\"notes-heading-{{{{ fhir.path resource \"id\" }}}}\">{{{{ l10n.t \"{resourceName}.Notes\" }}}}</h4>");
            sb.AppendLine($"    <ul>");
            sb.AppendLine($"    {{{{~ for note in (fhir.path resource \"note\") ~}}}}");
            sb.AppendLine($"      <li>{{{{ fhir.path note \"text\" }}}}</li>");
            sb.AppendLine($"    {{{{~ end ~}}}}");
            sb.AppendLine($"    </ul>");
            sb.AppendLine($"  </section>");
            sb.AppendLine($"  {{{{~ end ~}}}}");
            resxEntries[$"{resourceName}.Notes"] = "Notes";
        }

        sb.AppendLine("</div>");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a template section for a single element.
    /// </summary>
    private void GenerateElementSection(
        StringBuilder sb,
        string resourceName,
        ElementDefinition element,
        Dictionary<string, string> resxEntries)
    {
        string elementName = element.cgName();
        string elementPath = element.Path;
        bool isArray = element.cgIsArray();
        bool isRequired = element.cgCardinalityMin() > 0;

        // Get the primary type
        var primaryType = element.Type.FirstOrDefault();
        string typeCode = primaryType?.Code ?? "string";

        // Generate localization key
        string labelKey = $"{resourceName}.{ToPascalCase(elementName)}";
        string labelValue = ToHumanReadable(elementName);
        resxEntries[labelKey] = labelValue;

        // Start conditional block (unless required)
        if (!isRequired)
        {
            sb.AppendLine($"    {{{{~ if (fhir.exists resource \"{elementName}\") ~}}}}");
        }

        // Generate based on type
        if (isArray)
        {
            GenerateArrayElement(sb, resourceName, elementName, typeCode, labelKey);
        }
        else
        {
            GenerateScalarElement(sb, resourceName, elementName, typeCode, labelKey);
        }

        // End conditional block
        if (!isRequired)
        {
            sb.AppendLine($"    {{{{~ end ~}}}}");
        }
    }

    /// <summary>
    /// Generates template code for a scalar (non-array) element.
    /// </summary>
    private void GenerateScalarElement(
        StringBuilder sb,
        string resourceName,
        string elementName,
        string typeCode,
        string labelKey)
    {
        sb.AppendLine($"    <dt>{{{{ l10n.t \"{labelKey}\" }}}}</dt>");

        switch (typeCode)
        {
            case "dateTime":
            case "instant":
                sb.AppendLine($"    <dd>{{{{ fhir.format_datetime (fhir.path resource \"{elementName}\") }}}}</dd>");
                break;

            case "date":
                sb.AppendLine($"    <dd>{{{{ fhir.format_date (fhir.path resource \"{elementName}\") }}}}</dd>");
                break;

            case "Period":
                // Build period display inline (start - end)
                sb.AppendLine($"    {{{{~ period_start = fhir.path resource \"{elementName}.start\" ~}}}}");
                sb.AppendLine($"    {{{{~ period_end = fhir.path resource \"{elementName}.end\" ~}}}}");
                sb.AppendLine($"    <dd>");
                sb.AppendLine($"      {{{{~ if period_start && period_start != \"\" ~}}}}{{{{ fhir.format_date period_start }}}}{{{{~ end ~}}}}");
                sb.AppendLine($"      {{{{~ if (period_start && period_start != \"\") && (period_end && period_end != \"\") ~}}}} - {{{{~ end ~}}}}");
                sb.AppendLine($"      {{{{~ if period_end && period_end != \"\" ~}}}}{{{{ fhir.format_date period_end }}}}{{{{~ end ~}}}}");
                sb.AppendLine($"    </dd>");
                break;

            case "CodeableConcept":
                // Use display helper for CodeableConcept (this helper exists)
                sb.AppendLine($"    {{{{~ cc_display = fhir.path resource \"{elementName}.coding.first().display\" ~}}}}");
                sb.AppendLine($"    {{{{~ cc_code = fhir.path resource \"{elementName}.coding.first().code\" ~}}}}");
                sb.AppendLine($"    <dd>{{{{ (cc_display && cc_display != \"\") ? cc_display : cc_code }}}}</dd>");
                break;

            case "Coding":
                // Use display helper for Coding (this helper exists)
                sb.AppendLine($"    {{{{~ coding_display = fhir.path resource \"{elementName}.display\" ~}}}}");
                sb.AppendLine($"    {{{{~ coding_code = fhir.path resource \"{elementName}.code\" ~}}}}");
                sb.AppendLine($"    <dd>{{{{ (coding_display && coding_display != \"\") ? coding_display : coding_code }}}}</dd>");
                break;

            case "Reference":
                // Build reference display inline (display or reference)
                sb.AppendLine($"    {{{{~ ref_display = fhir.path resource \"{elementName}.display\" ~}}}}");
                sb.AppendLine($"    {{{{~ ref_reference = fhir.path resource \"{elementName}.reference\" ~}}}}");
                sb.AppendLine($"    <dd>{{{{ (ref_display && ref_display != \"\") ? ref_display : ref_reference }}}}</dd>");
                break;

            case "Quantity":
                // Build quantity display inline (value unit)
                sb.AppendLine($"    {{{{~ qty_value = fhir.path resource \"{elementName}.value\" ~}}}}");
                sb.AppendLine($"    {{{{~ qty_unit = fhir.path resource \"{elementName}.unit\" ~}}}}");
                sb.AppendLine($"    {{{{~ qty_code = fhir.path resource \"{elementName}.code\" ~}}}}");
                sb.AppendLine($"    <dd>{{{{ qty_value }}}}{{{{~ if qty_unit && qty_unit != \"\" ~}}}} {{{{ qty_unit }}}}{{{{~ else if qty_code && qty_code != \"\" ~}}}} {{{{ qty_code }}}}{{{{~ end ~}}}}</dd>");
                break;

            case "HumanName":
                // Build name display inline (given family)
                sb.AppendLine($"    {{{{~ name_given = fhir.path resource \"{elementName}.given.first()\" ~}}}}");
                sb.AppendLine($"    {{{{~ name_family = fhir.path resource \"{elementName}.family\" ~}}}}");
                sb.AppendLine($"    <dd>{{{{ name_given }}}}{{{{~ if name_given && name_given != \"\" && name_family && name_family != \"\" ~}}}} {{{{~ end ~}}}}{{{{ name_family }}}}</dd>");
                break;

            case "Address":
                // Build address display inline (city, state postal)
                sb.AppendLine($"    {{{{~ addr_city = fhir.path resource \"{elementName}.city\" ~}}}}");
                sb.AppendLine($"    {{{{~ addr_state = fhir.path resource \"{elementName}.state\" ~}}}}");
                sb.AppendLine($"    {{{{~ addr_postal = fhir.path resource \"{elementName}.postalCode\" ~}}}}");
                sb.AppendLine($"    <dd>");
                sb.AppendLine($"      {{{{~ if addr_city && addr_city != \"\" ~}}}}{{{{ addr_city }}}}{{{{~ end ~}}}}");
                sb.AppendLine($"      {{{{~ if addr_state && addr_state != \"\" ~}}}}{{{{~ if addr_city && addr_city != \"\" ~}}}}, {{{{~ end ~}}}}{{{{ addr_state }}}}{{{{~ end ~}}}}");
                sb.AppendLine($"      {{{{~ if addr_postal && addr_postal != \"\" ~}}}} {{{{ addr_postal }}}}{{{{~ end ~}}}}");
                sb.AppendLine($"    </dd>");
                break;

            case "Identifier":
                // Build identifier display inline (type: value)
                sb.AppendLine($"    {{{{~ id_value = fhir.path resource \"{elementName}.value\" ~}}}}");
                sb.AppendLine($"    {{{{~ id_system = fhir.path resource \"{elementName}.system\" ~}}}}");
                sb.AppendLine($"    {{{{~ id_type = fhir.path resource \"{elementName}.type.coding.first().display\" ~}}}}");
                sb.AppendLine($"    <dd>");
                sb.AppendLine($"      {{{{~ if id_type && id_type != \"\" ~}}}}<span class=\"identifier-type\">{{{{ id_type }}}}:</span> {{{{~ end ~}}}}");
                sb.AppendLine($"      <span class=\"identifier-value\">{{{{ id_value }}}}</span>");
                sb.AppendLine($"      {{{{~ if id_system && id_system != \"\" ~}}}} <span class=\"identifier-system\">({{{{ id_system }}}})</span>{{{{~ end ~}}}}");
                sb.AppendLine($"    </dd>");
                break;

            case "ContactPoint":
                // Build contact point display inline (system: value)
                sb.AppendLine($"    {{{{~ cp_system = fhir.path resource \"{elementName}.system\" ~}}}}");
                sb.AppendLine($"    {{{{~ cp_value = fhir.path resource \"{elementName}.value\" ~}}}}");
                sb.AppendLine($"    {{{{~ cp_use = fhir.path resource \"{elementName}.use\" ~}}}}");
                sb.AppendLine($"    <dd>");
                sb.AppendLine($"      {{{{~ if cp_system && cp_system != \"\" ~}}}}{{{{ l10n.t (\"Telecom.System.\" + cp_system) }}}}: {{{{~ end ~}}}}");
                sb.AppendLine($"      {{{{ cp_value }}}}");
                sb.AppendLine($"      {{{{~ if cp_use && cp_use != \"\" ~}}}} ({{{{ l10n.t (\"Telecom.Use.\" + cp_use) }}}}){{{{~ end ~}}}}");
                sb.AppendLine($"    </dd>");
                break;

            case "boolean":
                // Build boolean display inline using localization
                sb.AppendLine($"    {{{{~ bool_value = fhir.path resource \"{elementName}\" ~}}}}");
                sb.AppendLine($"    <dd>");
                sb.AppendLine($"      {{{{~ if bool_value == \"true\" ~}}}}");
                sb.AppendLine($"        {{{{ l10n.t \"Common.Yes\" }}}}");
                sb.AppendLine($"      {{{{~ else ~}}}}");
                sb.AppendLine($"        {{{{ l10n.t \"Common.No\" }}}}");
                sb.AppendLine($"      {{{{~ end ~}}}}");
                sb.AppendLine($"    </dd>");
                break;

            case "code":
            case "string":
            case "uri":
            case "url":
            case "canonical":
            case "markdown":
            default:
                sb.AppendLine($"    <dd>{{{{ fhir.path resource \"{elementName}\" }}}}</dd>");
                break;
        }
    }

    /// <summary>
    /// Generates template code for an array element.
    /// </summary>
    private void GenerateArrayElement(
        StringBuilder sb,
        string resourceName,
        string elementName,
        string typeCode,
        string labelKey)
    {
        sb.AppendLine($"    <dt>{{{{ l10n.t \"{labelKey}\" }}}}</dt>");
        sb.AppendLine($"    <dd>");
        sb.AppendLine($"      <ul class=\"fhir-list\">");
        sb.AppendLine($"      {{{{~ item_count = fhir.count resource \"{elementName}\" ~}}}}");
        sb.AppendLine($"      {{{{~ for i in 0..(item_count - 1) ~}}}}");

        switch (typeCode)
        {
            case "CodeableConcept":
                // Build CodeableConcept display inline
                sb.AppendLine($"      {{{{~ cc_display = fhir.path resource (\"{elementName}[\" + i + \"].coding.first().display\") ~}}}}");
                sb.AppendLine($"      {{{{~ cc_code = fhir.path resource (\"{elementName}[\" + i + \"].coding.first().code\") ~}}}}");
                sb.AppendLine($"        <li>{{{{ (cc_display && cc_display != \"\") ? cc_display : cc_code }}}}</li>");
                break;

            case "Coding":
                // Build Coding display inline
                sb.AppendLine($"      {{{{~ coding_display = fhir.path resource (\"{elementName}[\" + i + \"].display\") ~}}}}");
                sb.AppendLine($"      {{{{~ coding_code = fhir.path resource (\"{elementName}[\" + i + \"].code\") ~}}}}");
                sb.AppendLine($"        <li>{{{{ (coding_display && coding_display != \"\") ? coding_display : coding_code }}}}</li>");
                break;

            case "Reference":
                // Build Reference display inline
                sb.AppendLine($"      {{{{~ ref_display = fhir.path resource (\"{elementName}[\" + i + \"].display\") ~}}}}");
                sb.AppendLine($"      {{{{~ ref_reference = fhir.path resource (\"{elementName}[\" + i + \"].reference\") ~}}}}");
                sb.AppendLine($"        <li>{{{{ (ref_display && ref_display != \"\") ? ref_display : ref_reference }}}}</li>");
                break;

            case "Identifier":
                // Build Identifier display inline
                sb.AppendLine($"      {{{{~ id_value = fhir.path resource (\"{elementName}[\" + i + \"].value\") ~}}}}");
                sb.AppendLine($"      {{{{~ id_system = fhir.path resource (\"{elementName}[\" + i + \"].system\") ~}}}}");
                sb.AppendLine($"      {{{{~ id_type = fhir.path resource (\"{elementName}[\" + i + \"].type.coding.first().display\") ~}}}}");
                sb.AppendLine($"        <li>");
                sb.AppendLine($"          {{{{~ if id_type && id_type != \"\" ~}}}}<span class=\"identifier-type\">{{{{ id_type }}}}:</span> {{{{~ end ~}}}}");
                sb.AppendLine($"          <span class=\"identifier-value\">{{{{ id_value }}}}</span>");
                sb.AppendLine($"          {{{{~ if id_system && id_system != \"\" ~}}}} <span class=\"identifier-system\">({{{{ id_system }}}})</span>{{{{~ end ~}}}}");
                sb.AppendLine($"        </li>");
                break;

            case "HumanName":
                // Build HumanName display inline
                sb.AppendLine($"      {{{{~ name_given = fhir.path resource (\"{elementName}[\" + i + \"].given.first()\") ~}}}}");
                sb.AppendLine($"      {{{{~ name_family = fhir.path resource (\"{elementName}[\" + i + \"].family\") ~}}}}");
                sb.AppendLine($"        <li>{{{{ name_given }}}}{{{{~ if name_given && name_given != \"\" && name_family && name_family != \"\" ~}}}} {{{{~ end ~}}}}{{{{ name_family }}}}</li>");
                break;

            case "Address":
                // Build Address display inline
                sb.AppendLine($"      {{{{~ addr_city = fhir.path resource (\"{elementName}[\" + i + \"].city\") ~}}}}");
                sb.AppendLine($"      {{{{~ addr_state = fhir.path resource (\"{elementName}[\" + i + \"].state\") ~}}}}");
                sb.AppendLine($"      {{{{~ addr_postal = fhir.path resource (\"{elementName}[\" + i + \"].postalCode\") ~}}}}");
                sb.AppendLine($"        <li>");
                sb.AppendLine($"          {{{{~ if addr_city && addr_city != \"\" ~}}}}{{{{ addr_city }}}}{{{{~ end ~}}}}");
                sb.AppendLine($"          {{{{~ if addr_state && addr_state != \"\" ~}}}}{{{{~ if addr_city && addr_city != \"\" ~}}}}, {{{{~ end ~}}}}{{{{ addr_state }}}}{{{{~ end ~}}}}");
                sb.AppendLine($"          {{{{~ if addr_postal && addr_postal != \"\" ~}}}} {{{{ addr_postal }}}}{{{{~ end ~}}}}");
                sb.AppendLine($"        </li>");
                break;

            case "ContactPoint":
                // Build ContactPoint display inline
                sb.AppendLine($"      {{{{~ cp_system = fhir.path resource (\"{elementName}[\" + i + \"].system\") ~}}}}");
                sb.AppendLine($"      {{{{~ cp_value = fhir.path resource (\"{elementName}[\" + i + \"].value\") ~}}}}");
                sb.AppendLine($"      {{{{~ cp_use = fhir.path resource (\"{elementName}[\" + i + \"].use\") ~}}}}");
                sb.AppendLine($"        <li>");
                sb.AppendLine($"          {{{{~ if cp_system && cp_system != \"\" ~}}}}{{{{ l10n.t (\"Telecom.System.\" + cp_system) }}}}: {{{{~ end ~}}}}");
                sb.AppendLine($"          {{{{ cp_value }}}}");
                sb.AppendLine($"          {{{{~ if cp_use && cp_use != \"\" ~}}}} ({{{{ l10n.t (\"Telecom.Use.\" + cp_use) }}}}){{{{~ end ~}}}}");
                sb.AppendLine($"        </li>");
                break;

            case "Quantity":
                // Build Quantity display inline
                sb.AppendLine($"      {{{{~ qty_value = fhir.path resource (\"{elementName}[\" + i + \"].value\") ~}}}}");
                sb.AppendLine($"      {{{{~ qty_unit = fhir.path resource (\"{elementName}[\" + i + \"].unit\") ~}}}}");
                sb.AppendLine($"      {{{{~ qty_code = fhir.path resource (\"{elementName}[\" + i + \"].code\") ~}}}}");
                sb.AppendLine($"        <li>{{{{ qty_value }}}}{{{{~ if qty_unit && qty_unit != \"\" ~}}}} {{{{ qty_unit }}}}{{{{~ else if qty_code && qty_code != \"\" ~}}}} {{{{ qty_code }}}}{{{{~ end ~}}}}</li>");
                break;

            case "Period":
                // Build Period display inline
                sb.AppendLine($"      {{{{~ period_start = fhir.path resource (\"{elementName}[\" + i + \"].start\") ~}}}}");
                sb.AppendLine($"      {{{{~ period_end = fhir.path resource (\"{elementName}[\" + i + \"].end\") ~}}}}");
                sb.AppendLine($"        <li>");
                sb.AppendLine($"          {{{{~ if period_start && period_start != \"\" ~}}}}{{{{ fhir.format_date period_start }}}}{{{{~ end ~}}}}");
                sb.AppendLine($"          {{{{~ if (period_start && period_start != \"\") && (period_end && period_end != \"\") ~}}}} - {{{{~ end ~}}}}");
                sb.AppendLine($"          {{{{~ if period_end && period_end != \"\" ~}}}}{{{{ fhir.format_date period_end }}}}{{{{~ end ~}}}}");
                sb.AppendLine($"        </li>");
                break;

            default:
                sb.AppendLine($"      {{{{~ item_value = fhir.path resource (\"{elementName}[\" + i + \"]\") ~}}}}");
                sb.AppendLine($"        <li>{{{{ item_value }}}}</li>");
                break;
        }

        sb.AppendLine($"      {{{{~ end ~}}}}");
        sb.AppendLine($"      </ul>");
        sb.AppendLine($"    </dd>");
    }

    /// <summary>
    /// Generates a .resx file with localization entries.
    /// </summary>
    private void GenerateResxFile(string filePath, Dictionary<string, string> entries)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<root>");
        sb.AppendLine("  <!-- Auto-generated localization file for FHIR narrative templates -->");
        sb.AppendLine("  <xsd:schema id=\"root\" xmlns=\"\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:msdata=\"urn:schemas-microsoft-com:xml-msdata\">");
        sb.AppendLine("    <xsd:element name=\"root\" msdata:IsDataSet=\"true\">");
        sb.AppendLine("      <xsd:complexType>");
        sb.AppendLine("        <xsd:choice maxOccurs=\"unbounded\">");
        sb.AppendLine("          <xsd:element name=\"data\">");
        sb.AppendLine("            <xsd:complexType>");
        sb.AppendLine("              <xsd:sequence>");
        sb.AppendLine("                <xsd:element name=\"value\" type=\"xsd:string\" minOccurs=\"0\" msdata:Ordinal=\"1\" />");
        sb.AppendLine("                <xsd:element name=\"comment\" type=\"xsd:string\" minOccurs=\"0\" msdata:Ordinal=\"2\" />");
        sb.AppendLine("              </xsd:sequence>");
        sb.AppendLine("              <xsd:attribute name=\"name\" type=\"xsd:string\" use=\"required\" />");
        sb.AppendLine("            </xsd:complexType>");
        sb.AppendLine("          </xsd:element>");
        sb.AppendLine("        </xsd:choice>");
        sb.AppendLine("      </xsd:complexType>");
        sb.AppendLine("    </xsd:element>");
        sb.AppendLine("  </xsd:schema>");
        sb.AppendLine("  <resheader name=\"resmimetype\"><value>text/microsoft-resx</value></resheader>");
        sb.AppendLine("  <resheader name=\"version\"><value>2.0</value></resheader>");
        sb.AppendLine("  <resheader name=\"reader\"><value>System.Resources.ResXResourceReader</value></resheader>");
        sb.AppendLine("  <resheader name=\"writer\"><value>System.Resources.ResXResourceWriter</value></resheader>");

        foreach (var (key, value) in entries.OrderBy(e => e.Key))
        {
            sb.AppendLine($"  <data name=\"{EscapeXml(key)}\" xml:space=\"preserve\">");
            sb.AppendLine($"    <value>{EscapeXml(value)}</value>");
            sb.AppendLine($"  </data>");
        }

        sb.AppendLine("</root>");

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Generates a C# template loader class.
    /// </summary>
    private void GenerateTemplateLoader(
        string filePath,
        string fhirVersion,
        DefinitionCollection definitions,
        string namespaceName)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// <copyright file=\"" + Path.GetFileName(filePath) + "\" company=\"Microsoft Corporation\">");
        sb.AppendLine("//     Copyright (c) Microsoft Corporation. All rights reserved.");
        sb.AppendLine("//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.");
        sb.AppendLine("// </copyright>");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Collections.Immutable;");
        sb.AppendLine("using System.IO;");
        sb.AppendLine("using System.Reflection;");
        sb.AppendLine();
        sb.AppendLine($"namespace {namespaceName};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Pre-generated narrative template loader for FHIR {fhirVersion}.");
        sb.AppendLine("/// Provides efficient lookup of Scriban templates by resource type.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public static class {fhirVersion}NarrativeTemplateLoader");
        sb.AppendLine("{");

        // Generate resource type set
        var resourceTypes = definitions.ResourcesByName
            .Where(kvp => kvp.Value.Abstract != true)
            .Select(kvp => kvp.Key)
            .OrderBy(name => name)
            .ToList();

        sb.AppendLine("    /// <summary>Gets the set of supported resource types.</summary>");
        sb.AppendLine("    public static IReadOnlySet<string> SupportedResourceTypes { get; } = new HashSet<string>");
        sb.AppendLine("    {");
        foreach (var resourceType in resourceTypes)
        {
            sb.AppendLine($"        \"{resourceType}\",");
        }
        sb.AppendLine("    }.ToImmutableHashSet();");
        sb.AppendLine();

        // Template path mapping
        sb.AppendLine("    /// <summary>Gets the template folder for a resource type (Normative or version-specific).</summary>");
        sb.AppendLine("    private static readonly Dictionary<string, string> _templateFolders = new()");
        sb.AppendLine("    {");
        foreach (var resourceType in resourceTypes)
        {
            var sd = definitions.ResourcesByName[resourceType];
            bool isNormative = IsNormativeResource(resourceType, sd);
            string folder = isNormative ? "Normative" : fhirVersion;
            sb.AppendLine($"        [\"{resourceType}\"] = \"{folder}\",");
        }
        sb.AppendLine("    };");
        sb.AppendLine();

        // HasTemplate method
        sb.AppendLine("    /// <summary>Checks if a template exists for the specified resource type.</summary>");
        sb.AppendLine("    /// <param name=\"resourceType\">The FHIR resource type name.</param>");
        sb.AppendLine("    /// <returns>True if a template exists, false otherwise.</returns>");
        sb.AppendLine("    public static bool HasTemplate(string resourceType)");
        sb.AppendLine("    {");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(resourceType);");
        sb.AppendLine("        return SupportedResourceTypes.Contains(resourceType);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // GetTemplatePath method
        sb.AppendLine("    /// <summary>Gets the relative path to the template file for a resource type.</summary>");
        sb.AppendLine("    /// <param name=\"resourceType\">The FHIR resource type name.</param>");
        sb.AppendLine("    /// <returns>The relative path to the template file, or null if not found.</returns>");
        sb.AppendLine("    public static string? GetTemplatePath(string resourceType)");
        sb.AppendLine("    {");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(resourceType);");
        sb.AppendLine("        ");
        sb.AppendLine("        if (!_templateFolders.TryGetValue(resourceType, out var folder))");
        sb.AppendLine("        {");
        sb.AppendLine("            return null;");
        sb.AppendLine("        }");
        sb.AppendLine("        ");
        sb.AppendLine("        return $\"Templates/{folder}/{resourceType}.scriban\";");
        sb.AppendLine("    }");
        sb.AppendLine();

        // LoadTemplate method
        sb.AppendLine("    /// <summary>Loads the template content for a resource type from embedded resources.</summary>");
        sb.AppendLine("    /// <param name=\"resourceType\">The FHIR resource type name.</param>");
        sb.AppendLine("    /// <returns>The template content, or null if not found.</returns>");
        sb.AppendLine("    public static string? LoadTemplate(string resourceType)");
        sb.AppendLine("    {");
        sb.AppendLine("        var path = GetTemplatePath(resourceType);");
        sb.AppendLine("        if (path == null)");
        sb.AppendLine("        {");
        sb.AppendLine("            return null;");
        sb.AppendLine("        }");
        sb.AppendLine("        ");
        sb.AppendLine("        // Attempt to load from embedded resource");
        sb.AppendLine("        var assembly = typeof(" + fhirVersion + "NarrativeTemplateLoader).Assembly;");
        sb.AppendLine("        var resourceName = assembly.GetName().Name + \".\" + path.Replace('/', '.').Replace('\\\\', '.');");
        sb.AppendLine("        ");
        sb.AppendLine("        using var stream = assembly.GetManifestResourceStream(resourceName);");
        sb.AppendLine("        if (stream == null)");
        sb.AppendLine("        {");
        sb.AppendLine("            return null;");
        sb.AppendLine("        }");
        sb.AppendLine("        ");
        sb.AppendLine("        using var reader = new StreamReader(stream);");
        sb.AppendLine("        return reader.ReadToEnd();");
        sb.AppendLine("    }");
        sb.AppendLine();

        // GetAllTemplates method
        sb.AppendLine("    /// <summary>Gets all available template paths.</summary>");
        sb.AppendLine("    /// <returns>An enumerable of (resourceType, templatePath) tuples.</returns>");
        sb.AppendLine("    public static IEnumerable<(string ResourceType, string TemplatePath)> GetAllTemplatePaths()");
        sb.AppendLine("    {");
        sb.AppendLine("        foreach (var resourceType in SupportedResourceTypes)");
        sb.AppendLine("        {");
        sb.AppendLine("            var path = GetTemplatePath(resourceType);");
        sb.AppendLine("            if (path != null)");
        sb.AppendLine("            {");
        sb.AppendLine("                yield return (resourceType, path);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");

        sb.AppendLine("}");

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Converts a FHIR element name to PascalCase.
    /// </summary>
    private string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        // Handle [x] suffix for choice types
        if (input.EndsWith("[x]", StringComparison.Ordinal))
        {
            input = input[..^3];
        }

        return char.ToUpperInvariant(input[0]) + input[1..];
    }

    /// <summary>
    /// Converts a camelCase element name to a human-readable label.
    /// </summary>
    private string ToHumanReadable(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        // Handle [x] suffix for choice types
        if (input.EndsWith("[x]", StringComparison.Ordinal))
        {
            input = input[..^3];
        }

        var sb = new StringBuilder();
        sb.Append(char.ToUpperInvariant(input[0]));

        for (int i = 1; i < input.Length; i++)
        {
            if (char.IsUpper(input[i]))
            {
                sb.Append(' ');
            }

            sb.Append(input[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Escapes a string for use in XML.
    /// </summary>
    private string EscapeXml(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}

/// <summary>
/// Configuration for the Scriban Narrative Template generator.
/// </summary>
public sealed class CSharpNarrativeTemplateConfig
{
    /// <summary>Gets or sets the output directory for generated files.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the namespace for generated classes.</summary>
    public string Namespace { get; set; } = "Ignixa.NarrativeGenerator.Generated";
}

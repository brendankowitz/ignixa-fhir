# Ignixa.NarrativeGenerator

FHIR narrative generation using Scriban templates with FHIRPath support. Generates human-readable summaries of FHIR resources in multiple output formats (XHTML, Markdown, Compact).

## Why Use This Package?

- **Multi-format output**: Generate XHTML (FHIR-compliant), Markdown, or Compact (single-line) narratives
- **Template-based**: Uses Scriban templates for flexible, customizable output
- **FHIRPath integration**: Templates can use FHIRPath expressions for data extraction
- **XSS protection**: Built-in XHTML sanitization for secure HTML output
- **Localization support**: Full i18n support via `IStringLocalizer`
- **Version-aware**: Templates can be version-specific (R4, R4B, R5, R6, STU3)

## Installation

```bash
dotnet add package Ignixa.NarrativeGenerator
```

## Quick Start

### Basic Usage

```csharp
using Ignixa.NarrativeGenerator;
using Ignixa.Specification.Generated;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

// Create a schema and generator
var schema = new R4CoreSchemaProvider();
var generator = FhirNarrativeGenerator.Create(schema);

// Parse a FHIR resource
var json = """
    {
        "resourceType": "Patient",
        "id": "example",
        "name": [{
            "use": "official",
            "family": "Doe",
            "given": ["John"]
        }],
        "gender": "male",
        "birthDate": "1980-01-01"
    }
    """;

var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
patient.FhirVersion = FhirVersion.R4;
var element = patient.ToElement(schema);

// Generate narrative (defaults to XHTML)
var narrative = await generator.GenerateNarrativeAsync(element, "Patient");
// Result: Sanitized XHTML suitable for FHIR Narrative.div
```

### Multiple Output Formats

```csharp
// XHTML (default) - for FHIR Narrative.div
var xhtml = await generator.GenerateNarrativeAsync(
    element, "Patient", format: TemplateFormat.Html);

// Markdown - for documentation/display
var markdown = await generator.GenerateNarrativeAsync(
    element, "Patient", format: TemplateFormat.Markdown);

// Compact - single-line for AI/ML embeddings
var compact = await generator.GenerateNarrativeAsync(
    element, "Patient", format: TemplateFormat.Compact);
```

### Localization

```csharp
using System.Globalization;

// Generate with specific culture
var narrative = await generator.GenerateNarrativeAsync(
    element,
    "Patient",
    culture: new CultureInfo("es-ES"),
    format: TemplateFormat.Html);

// Or provide a custom localizer
var customLocalizer = new MyCustomStringLocalizer();
var generator = FhirNarrativeGenerator.Create(schema, customLocalizer);
```

## Output Formats

### Html (XHTML)
FHIR-compliant XHTML for `Narrative.div`. Includes:
- WCAG 2.1 AA accessibility
- XSS sanitization
- CSS classes for styling

### Markdown
Human-readable Markdown for documentation:
- Headers and sections
- Bold/italic formatting
- Lists and tables

### Compact
Single-line dense format for AI/ML:
- Token-efficient
- Structured data extraction
- Suitable for vector embeddings

## Template Resolution

Templates are resolved in this order:
1. Format-specific + Version-specific (e.g., `Patient.R4.html.scriban`)
2. Format-specific + Generic (e.g., `Patient.html.scriban`)
3. Generic fallback (e.g., `Generic.html.scriban`)

## FHIRPath in Templates

Templates can use FHIRPath expressions:

```scriban
{{ fhirpath "name.where(use='official').family" }}
{{ fhirpath "telecom.where(system='phone').value" }}
{{ fhirpath "birthDate" | date.to_string "%Y-%m-%d" }}
```

## Related Packages

- **Ignixa.Abstractions**: Provides `IElement` and `ISchema` interfaces
- **Ignixa.Serialization**: Parse JSON to `IElement` trees
- **Ignixa.FhirPath**: FHIRPath evaluation engine used by templates
- **Ignixa.Specification**: FHIR structure definitions and schema providers

## License

MIT License - see LICENSE file in repository root

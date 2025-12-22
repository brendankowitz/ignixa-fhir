---
sidebar_position: 8
title: Narrative Generator
description: Generate FHIR narrative text from resources
---

# Narrative Generator

The `Ignixa.NarrativeGenerator` package generates human-readable content for FHIR resources using Scriban templates with FHIRPath support. Supports multiple output formats including XHTML, Markdown, and compact embeddings.

## Installation

```bash
dotnet add package Ignixa.NarrativeGenerator
```

## Quick Start

```csharp
using Ignixa.NarrativeGenerator;

var schema = SchemaProvider.GetSchema(FhirVersion.R4);
var generator = FhirNarrativeGenerator.Create(schema);

// Generate XHTML narrative (default)
var html = await generator.GenerateNarrativeAsync(element, "Patient");

// Generate Markdown
var markdown = await generator.GenerateNarrativeAsync(
    element, "Patient", format: TemplateFormat.Markdown);

// Generate compact format for embeddings
var compact = await generator.GenerateNarrativeAsync(
    element, "Patient", format: TemplateFormat.Compact);
```

## Output Formats

### Html (Default)

Standard FHIR XHTML narrative for the `Narrative.div` element. Sanitized for XSS protection and WCAG 2.1 AA compliant.

```csharp
var html = await generator.GenerateNarrativeAsync(element, "Patient");
// Returns: <div xmlns="http://www.w3.org/1999/xhtml"><p>...</p></div>
```

### Markdown

Human-readable Markdown for documentation, clinical summaries, or Markdown-aware displays.

```csharp
var markdown = await generator.GenerateNarrativeAsync(
    element, "Patient", format: TemplateFormat.Markdown);
// Returns: ## Patient: John Smith\n**DOB:** 1980-01-15\n...
```

### Compact

Single-line, token-efficient format optimized for AI/ML embedding models. Produces dense text suitable for vector databases and semantic search.

```csharp
var compact = await generator.GenerateNarrativeAsync(
    element, "Patient", format: TemplateFormat.Compact);
// Returns: "45yo Male with Diabetes, Hypertension. Meds: Metformin 500mg, Lisinopril 10mg."
```

Use cases:
- Patient record vector embeddings
- Semantic search indexing
- RAG (Retrieval Augmented Generation) pipelines
- Clinical decision support context

## Features

- **Scriban Templates**: Use Scriban templating with FHIRPath expressions
- **Built-in Templates**: Default templates for common resource types
- **Custom Templates**: Define your own templates per resource type
- **FHIRPath Integration**: Access resource data using FHIRPath in templates
- **Localization**: i18n support via `IStringLocalizer`
- **XSS Protection**: Automatic sanitization for HTML output

## Template Example

```scriban
<div xmlns="http://www.w3.org/1999/xhtml">
  <p><b>Patient:</b> {{ select "name.first().text" }}</p>
  <p><b>DOB:</b> {{ select "birthDate" }}</p>
  {{ if is_true "active" }}
  <p>Status: Active</p>
  {{ end }}
</div>
```

## Localization

```csharp
var localizer = new ResourceManagerStringLocalizer(resourceManager);
var generator = FhirNarrativeGenerator.Create(schema, localizer);

var narrative = await generator.GenerateNarrativeAsync(
    element, "Patient", culture: new CultureInfo("es-ES"));
```

## Related Documentation

- [ADR: Narrative Generator](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2512-narrative-generator.md)

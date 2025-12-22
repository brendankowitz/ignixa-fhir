---
sidebar_position: 8
title: Narrative Generator
description: Generate FHIR narrative text from resources
---

# Narrative Generator

The `Ignixa.NarrativeGenerator` package generates human-readable narrative (HTML) for FHIR resources using Scriban templates with FHIRPath support.

## Installation

```bash
dotnet add package Ignixa.NarrativeGenerator
```

## Quick Start

```csharp
using Ignixa.NarrativeGenerator;

var generator = new NarrativeGenerator();
var narrative = await generator.GenerateAsync(patientResource);

// narrative.Div contains the HTML
// narrative.Status is "generated"
```

## Features

- **Scriban Templates**: Use Scriban templating with FHIRPath expressions
- **Built-in Templates**: Default templates for common resource types
- **Custom Templates**: Define your own templates per resource type
- **FHIRPath Integration**: Access resource data using FHIRPath in templates

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

## Related Documentation

- [ADR: Narrative Generator](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2512-narrative-generator.md)

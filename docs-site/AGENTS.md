# Documentation AI Agent Guidelines

This file provides guidance for AI agents (Claude, GPT, etc.) when generating or reviewing documentation for the Ignixa FHIR project.

## Azure OpenAI Configuration

When using Azure OpenAI for documentation generation, configure the following environment variables:

```bash
# Azure OpenAI Endpoint
AZURE_OPENAI_ENDPOINT=https://your-resource.openai.azure.com/

# API Key (or use Managed Identity)
AZURE_OPENAI_API_KEY=your-api-key

# Model deployment name (GPT-5 or GPT-4o recommended)
AZURE_OPENAI_DEPLOYMENT=gpt-5

# API Version
AZURE_OPENAI_API_VERSION=2024-02-15-preview
```

## Documentation Conventions

### Style Guidelines

1. **Be Direct** - Skip filler phrases like "Let's explore..." or "In this section..."
2. **Use Active Voice** - "Ignixa validates resources" not "Resources are validated by Ignixa"
3. **Code-First** - Show working examples before explaining concepts
4. **Healthcare Context** - Include FHIR-specific context (LOINC codes, references, etc.)

### Code Examples

- All code examples MUST be compilable/runnable
- Use realistic FHIR data (Patient names, LOINC codes, etc.)
- Include necessary using statements
- Show expected output where helpful

```csharp
// ✅ Good: Complete, runnable example
using Ignixa.Serialization;

var json = """{"resourceType": "Patient", "id": "123"}""";
var sourceNode = JsonSourceNavigator.Parse(json);
Console.WriteLine(sourceNode["id"].Text); // Output: 123

// ❌ Bad: Incomplete example
var sourceNode = Parse(json); // Missing context
```

### Documentation Structure

Each documentation page should follow this structure:

```markdown
---
sidebar_position: N
title: Short Title
description: One-line description for SEO
---

# Title

One paragraph overview (2-3 sentences max).

## Installation (if applicable)

\`\`\`bash
dotnet add package Ignixa.PackageName
\`\`\`

## Quick Start

Show the simplest working example first.

## Detailed Sections

Break down features, with code examples for each.

## Related Documentation

Links to related pages.
```

### FHIR-Specific Guidelines

1. **Resource Examples** - Use realistic but synthetic data
   - Patient: "John Smith", "Jane Doe" 
   - Observation: Use actual LOINC codes (29463-7 for Body Weight)
   - References: Always show full format `Patient/123`

2. **Version Awareness** - Note FHIR version differences where relevant
   - R4 vs R5 differences
   - Breaking changes between versions

3. **Compliance Notes** - Include relevant standards:
   - SMART on FHIR scopes
   - Bulk Data Access patterns
   - US Core profile requirements

### Terminology

Use consistent terminology:

| Use | Instead Of |
|-----|-----------|
| ISourceNode | source node, SourceNode |
| FHIRPath | FHIR Path, fhirpath |
| CapabilityStatement | Conformance (deprecated) |
| R4, R4B, R5 | FHIR 4.0, FHIR 4.3 |

## PR Documentation Updates

When code changes affect documentation:

1. **API Changes** - Update relevant Core SDK docs
2. **New Features** - Add to Server features section
3. **Breaking Changes** - Document migration path
4. **ADRs** - Create ADR for significant architectural decisions

## AI-Assisted Review Checklist

When reviewing documentation PRs:

- [ ] Code examples compile and run
- [ ] Links resolve correctly
- [ ] FHIR terminology is accurate
- [ ] No placeholder text remains
- [ ] Consistent with existing style
- [ ] Includes related documentation links

## Regenerating Documentation

To regenerate API documentation from code comments:

```bash
# From repository root
cd docs-site
npm run generate-api-docs
```

To validate all documentation links:

```bash
npm run build
```

## Contact

For documentation questions, open an issue with the `documentation` label.

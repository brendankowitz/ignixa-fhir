# Ignixa FHIR Documentation Site

Docusaurus-based documentation for the Ignixa FHIR project.

**Live Site**: https://brendankowitz.github.io/ignixa-fhir/

## Development

```bash
cd docs/site
npm install
npm start        # Development server at http://localhost:3000
npm run build    # Build static site
```

## Structure

```
docs/site/
├── docs/               # Documentation markdown files
│   ├── getting-started/  # Installation, quick start, configuration
│   ├── server/           # Server features and deployment
│   ├── core-sdk/         # SDK package documentation
│   └── adr/              # Architecture decision records (links to /docs/adr)
├── blog/               # Blog posts
├── src/                # Custom React components
└── static/             # Images and assets
```

## Documentation Guidelines

### Style

- **Direct**: Skip filler phrases like "Let's explore..." or "In this section..."
- **Active voice**: "Ignixa validates resources" not "Resources are validated"
- **Code-first**: Show working examples before explaining concepts
- **Healthcare context**: Include FHIR-specific details (LOINC codes, references)

### Code Examples

All code examples must be compilable/runnable:

```csharp
// Include using statements
using Ignixa.Serialization;

// Use realistic FHIR data
var json = """{"resourceType": "Patient", "id": "123"}""";
var sourceNode = JsonSourceNavigator.Parse(json);
Console.WriteLine(sourceNode["id"].Text); // Output: 123
```

### Page Structure

```markdown
---
sidebar_position: N
title: Short Title
description: One-line description for SEO
---

# Title

One paragraph overview (2-3 sentences max).

## Quick Start

Show the simplest working example first.

## Detailed Sections

Break down features with code examples.

## Related Documentation

Links to related pages.
```

### Terminology

| Use | Instead Of |
|-----|-----------|
| ISourceNode | source node, SourceNode |
| FHIRPath | FHIR Path, fhirpath |
| CapabilityStatement | Conformance (deprecated) |
| R4, R4B, R5 | FHIR 4.0, FHIR 4.3 |

## Deployment

Documentation deploys automatically:
- On push to `main` with changes in `docs/site/`
- When running the publish-release workflow
- Manually via `workflow_dispatch` on the docs workflow

## Adding Documentation

1. Create markdown file in appropriate `docs/` subdirectory
2. Add frontmatter with `sidebar_position`, `title`, `description`
3. Update `sidebars.js` if adding new section
4. Run `npm run build` to validate links

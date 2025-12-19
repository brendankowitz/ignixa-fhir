# ADR 2512: Narrative Generator Library

## Status
Accepted

## Context
FHIR narrative generation is required across multiple features:
- IPS Generator - Section narratives for patient summaries
- Resource Display - Human-readable rendering in UI/APIs
- Document Generation - Composition narratives, clinical notes
- Accessibility Compliance - WCAG 2.1 requirements for FHIR UIs

Without centralized infrastructure, each feature would implement its own templating, causing code duplication and inconsistent output.

## Decision
Implement **Ignixa.NarrativeGenerator** as a Core SDK package with tiered rendering:

```
┌─────────────────────────────────────────────┐
│          INarrativeGenerator                 │
│  GenerateNarrativeAsync(resource, options)  │
└──────────────────────┬──────────────────────┘
                       │
         ┌─────────────┴─────────────┐
         │                           │
┌────────▼────────┐      ┌───────────▼───────────┐
│ TemplateEngine  │      │  ToTextEngine         │
│ (Scriban)       │      │  (FHIRPath fallback)  │
│ Rich XHTML      │      │  Basic plain text     │
└─────────────────┘      └───────────────────────┘
```

**Template Organization:**
- Rich templates for normative resources (Patient, Observation, etc.)
- Basic ToText fallback for others
- Version-specific overrides (R4, R5) where needed
- User-provided custom templates via configuration

**Key Design Decisions:**
- **FHIRPath-first**: Use FHIRPath for data extraction, not brittle property navigation
- **Multi-version**: STU3, R4, R4B, R5 with version-aware template selection
- **Scriban templating**: Fast compilation, clean syntax, embedded expressions
- **Extensibility**: Override built-in templates or add custom ones

## Consequences

**Positive:**
- Centralized narrative infrastructure eliminates duplication
- FHIRPath-based templates work across FHIR versions
- Compiled templates provide good performance
- Extensible for custom deployments

**Negative:**
- Scriban dependency added to Core SDK
- Template maintenance required as FHIR evolves
- Rich templates only for subset of resource types

## References
- Investigation: `docs/features/fhir-operations/investigations/narrative-library.md`
- Implementation: PR #125 (Ignixa.NarrativeGenerator)

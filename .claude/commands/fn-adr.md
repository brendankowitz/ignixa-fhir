# Create ADR from Investigations

Synthesize viable investigations into a concise proposed ADR.

**Usage**: `/fn-adr {feature-name}`

## Instructions

1. **Read all investigations** in `docs/features/{feature-name}/investigations/`

2. **Identify viable investigations**: Status should be "Viable" or "In Progress" (not "Rejected")

3. **Generate ADR number**: Use format `{YYMM}` based on current date (e.g., 2512 for Dec 2025)

4. **Create ADR** at `docs/features/{feature-name}/adr-{YYMM}-{topic}.md`:

```markdown
# ADR-{YYMM}: {Title}

**Status**: Proposed
**Date**: {YYYY-MM-DD}
**Feature**: {feature-name}

## Context
{1-2 paragraphs: The problem from _feature.md, key constraints}

## Options Considered

1. **{Approach A}** - {one line summary} *(rejected: brief reason)*
2. **{Approach B}** - {one line summary} *(viable)*
3. **{Approach C}** - {one line summary} *(viable)*

## Decision

{1-2 paragraphs: What approach we chose and why. Reference the winning investigation(s).}

## Consequences

- {Impact on codebase}
- {What becomes easier}
- {What becomes harder or needs follow-up}
```

5. **Keep it concise**: No code snippets, no implementation checklists, no phase tracking. Just the decision and rationale.

6. **Update `readme.md`**:
   - Change status to "Decided"
   - Add link to ADR in Decision section
   - Mark synthesized investigations as "Merged"

7. **Output**: Show the ADR content and confirm next steps (implement, then `/fn-accept`)

## ADR Principles

- **Context**: Why we needed to decide (not how we'll implement)
- **Decision**: What we chose and why (not the full investigation details)
- **Consequences**: What changes as a result (both positive and negative)

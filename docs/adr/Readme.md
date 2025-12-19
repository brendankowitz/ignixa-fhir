# Architecture Decision Records

We will maintain Architecture Decision Records (ADRs) in our FHIR Server repository, placing each one under `docs/adr/adr-<yymm>-<short-title>.md`. We have chosen to write these records using Markdown.

Each ADR will be assigned a unique, sequential number (date based as above) that will never be reused. If a decision is later reversed, changed or evolved, the original record will remain in place but will be marked as superseded. Even though it's no longer valid, it is still historically important.

## Core Design Principle: F5 Developer Experience

All architectural decisions should support the principle that **a developer can press F5 and run the solution with minimal setup**. This means:

- **No complex infrastructure requirements**: The solution should run with in-memory or file-based storage by default
- **Self-contained dependencies**: All required resources should be embedded or easily available
- **Clear configuration**: Sensible defaults that "just work" for local development
- **Optional production dependencies**: Advanced features (CosmosDB, SQL Server, etc.) should be opt-in, not required

This principle ensures rapid onboarding, efficient debugging, and a smooth development workflow. Production-ready features should be additive, not prerequisites.

## ADR Format

Keep ADRs **concise** (40-100 lines). Focus on the decision and rationale, not implementation details.

### Template

```markdown
# ADR {YYMM}: {Short Title}

## Status
Proposed | Accepted | Deprecated | Superseded

## Context
What problem are we solving? Why is this decision needed? (2-3 sentences)

## Decision
What did we decide? Use bullet points for clarity:
- Key choice 1
- Key choice 2
- Architecture diagram (mermaid) if it aids understanding

## Consequences

**Positive:**
- Benefit 1
- Benefit 2

**Negative:**
- Trade-off 1
- Trade-off 2
```

### Good Examples

- **Internal**: [adr-2512-member-match-operation](adr-2512-member-match-operation.md) - Clean problem/solution/outcomes structure
- **External**: [microsoft/fhir-server ADR-2503](https://github.com/microsoft/fhir-server/blob/main/docs/arch/adr-2503-Bundle-include-operation.md) - Simple and direct

### What NOT to Include

- Full code implementations (link to source instead)
- Week-by-week implementation plans
- Detailed testing strategies
- Phase numbers or timeline estimates

This is inspired by [documenting architecture decisions](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions).

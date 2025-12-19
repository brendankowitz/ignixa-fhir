# Feature Development Workflow

This folder contains feature areas organized using a structured investigation-to-decision workflow.

## Quick Reference

| Command | Purpose |
|---------|---------|
| `/fn-feature {name}` | Create a new feature area |
| `/fn-investigation {feature} {topic}` | Add an investigation to explore an approach |
| `/fn-adr {feature}` | Synthesize viable investigations into a proposed ADR |
| `/fn-accept {feature} {adr-filename}` | Move implemented ADR to `docs/adr/` |
| `/fn-reject {feature} {investigation}` | Mark an investigation as rejected |

## Workflow

```
1. /fn-feature bulk-export
   └── Creates docs/features/bulk-export/readme.md

2. /fn-investigation bulk-export channel-based
   └── Creates docs/features/bulk-export/investigations/channel-based.md

3. /fn-investigation bulk-export streaming-export
   └── Creates docs/features/bulk-export/investigations/streaming-export.md

4. /fn-adr bulk-export
   └── Creates docs/features/bulk-export/adr-2512-bulk-export.md

5. (Implement the feature)

6. /fn-accept bulk-export adr-2512-bulk-export
   └── Moves to docs/adr/adr-2512-bulk-export.md
```

## Folder Structure

```
docs/features/
├── readme.md                    # This file
├── {feature-name}/
│   ├── readme.md                # Feature overview, status, constraints
│   ├── investigations/          # Exploration documents
│   │   ├── {approach-1}.md
│   │   └── {approach-2}.md
│   └── adr-{YYMM}-{topic}.md    # Proposed ADR (before acceptance)
```

## Naming Conventions

| Item | Convention | Example |
|------|------------|---------|
| Feature folder | kebab-case | `bulk-import`, `smart-on-fhir` |
| Investigation file | kebab-case, descriptive | `import-operation.md`, `channel-based.md` |
| ADR file | `adr-{YYMM}-{topic}.md` | `adr-2512-bulk-export.md` |

## Investigation Status Values

| Status | Meaning |
|--------|---------|
| `In Progress` | Currently being researched |
| `Viable` | Research complete, approach is valid |
| `Complete` | Research complete, ready for ADR synthesis |
| `Merged` | Incorporated into accepted ADR and implemented |
| `Rejected` | Not viable (use `/fn-reject` to document why) |

## Feature Status Values

| Status | Meaning |
|--------|---------|
| `Exploring` | Initial investigation phase |
| `Proposed` | ADR drafted, awaiting implementation |
| `Decided` | ADR accepted, implementation in progress |
| `Partial Implementation` | Some parts implemented |
| `Complete` | Fully implemented and accepted |

## Investigation File Template

```markdown
# Investigation: {Topic}

**Feature**: {feature-name}
**Status**: In Progress
**Created**: {YYYY-MM-DD}

## Approach
{What would we build, how would it work?}

## Tradeoffs

| Pros | Cons |
|------|------|
| {benefit} | {drawback} |

## Alignment

- [ ] Follows layer rules (API -> App -> Domain -> Data)
- [ ] F5 Developer Experience (works with minimal setup)
- [ ] FHIR spec compliance (if applicable)
- [ ] Consistent with existing patterns

## Evidence
{Research findings, code exploration, prior art}

## Verdict
*Pending evaluation*
```

## Tips for Claude Code

1. **Starting a new feature**: Use `/fn-feature {name}` first, then add investigations
2. **Multiple approaches**: Create separate investigation files for each approach
3. **Ready to decide**: When investigations are complete, use `/fn-adr` to synthesize
4. **After implementation**: Use `/fn-accept` to finalize the ADR
5. **Dead ends**: Use `/fn-reject` to document why an approach didn't work

## Current Features

| Feature | Status | Investigations |
|---------|--------|----------------|
| [architecture](architecture/) | Research | v2-architecture, core-shims, jsonobject-based, +7 more |
| [authorization](authorization/) | Viable | rbac-capabilities |
| [background-jobs](background-jobs/) | Complete | durabletask, watchdog-patterns |
| [bulk-import](bulk-import/) | Proposed | import-operation |
| [bundle-processing](bundle-processing/) | Research | architecture, streaming, deferred-writes, +5 more |
| [caching](caching/) | Complete | architecture, abstraction-architecture |
| [conditional-operations](conditional-operations/) | Complete | conditional-crud |
| [deployment](deployment/) | Research | azure-container-apps |
| [e2e-testing](e2e-testing/) | Complete | gap-analysis, implementation-checklist, data-setup-patterns, analysis-readme |
| [export](export/) | Research | streaming-architecture, high-throughput-design, +2 more |
| [fhir-compatibility](fhir-compatibility/) | Proposed | compatibility-remediation |
| [fhir-faker](fhir-faker/) | Partial | layered-architecture, scenario-generation, +6 more |
| [fhir-operations](fhir-operations/) | In Progress | advanced-operations, patient-everything, ips-generator, +4 more |
| [fhirpath](fhirpath/) | In Progress | performance-optimization, gap-analysis |
| [history](history/) | Research | streaming-migration |
| [mcp-integration](mcp-integration/) | Proposed | mcp-overview, tool-design |
| [multi-tenancy](multi-tenancy/) | Research | partitioning-modes, tenant-providers |
| [package-management](package-management/) | Research | npm-simplifier, multi-version-ig |
| [performance](performance/) | Active | post-put-analysis, version-override |
| [search](search/) | Partial | query-parsing, compartment-wildcard, +6 more |
| [serialization](serialization/) | Viable | model-refactoring, viewdefinition-support |
| [smart-on-fhir](smart-on-fhir/) | Research | identity-provider, v2-implementation |
| [status-reports](status-reports/) | Complete | october-2025-gaps, roadmap-gaps, legacy-analysis, +3 more |
| [storage](storage/) | Partial | ndjson-storage, v2-design, decoupled-indexing, file-based-search |
| [storage-cosmos](storage-cosmos/) | Research | petabyte-architecture, transaction-table, +2 more |
| [storage-sql](storage-sql/) | Partial | provider-architecture, connection-pooling, +4 more |
| [structuremap](structuremap/) | Approved | implementation-summary, operation-integration, mutation-strategy |
| [subscriptions](subscriptions/) | Investigation Complete | subscription-engine, transaction-table |
| [terminology-services](terminology-services/) | Proposed | terminology-operations, new-design |
| [unified-infrastructure](unified-infrastructure/) | Proposed | package-validation-integration |
| [validation](validation/) | Core Complete | validation-architecture, +7 more |

# Create Feature Area

Initialize a new feature area for exploration and decision-making.

**Usage**: `/fn-feature {feature-name}`

## Instructions

1. **Create folder structure**:
   - `docs/features/{feature-name}/`
   - `docs/features/{feature-name}/investigations/`

2. **Create `readme.md`** with this template:

```markdown
# Feature: {Name}

**Status**: Exploring
**Created**: {YYYY-MM-DD}

## Problem Statement
{Describe what problem this feature solves}

## Constraints
{Technical, organizational, or domain constraints that shape the solution}

## Investigations
| Investigation | Status | Summary |
|--------------|--------|---------|

## Decision
*No ADR yet - investigations in progress*
```

3. **Output**: Confirm the feature folder was created and summarize next steps (run `/fn-investigation` to explore approaches).

## Naming Convention

Use kebab-case for feature names: `bulk-import`, `auth-module`, `subscription-engine`

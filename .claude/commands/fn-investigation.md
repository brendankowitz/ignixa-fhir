# Create Investigation

Add an investigation exploring one approach within a feature area.

**Usage**: `/fn-investigation {feature-name} {investigation-topic}`

## Instructions

1. **Verify feature exists**: Check `docs/features/{feature-name}/readme.md` exists

2. **Create investigation file** at `docs/features/{feature-name}/investigations/{topic}.md`:

```markdown
# Investigation: {Topic}

**Feature**: {feature-name}
**Status**: In Progress
**Created**: {YYYY-MM-DD}

## Approach
{Describe this approach - what would we build, how would it work?}

## Tradeoffs

| Pros | Cons |
|------|------|
| {benefit} | {drawback} |

## Alignment

- [ ] Follows architectural layering rules
- [ ] Developer Experience (works with minimal setup)
- [ ] Specification compliance (if applicable)
- [ ] Consistent with existing patterns

## Evidence
{Research findings, code exploration, prior art from similar systems, relevant specs}

## Verdict
*Pending evaluation*
```

3. **Research the approach**:
   - Search codebase for related patterns
   - Check existing ADRs for relevant decisions
   - Look for prior art in similar systems if applicable
   - Document findings in Evidence section

4. **Generate alternatives** (quint-style): If this is the first investigation for a feature, briefly note 2-3 other approaches worth investigating. These become future `/fn-investigation` candidates.

5. **Update `readme.md`**: Add row to Investigations table with status "In Progress"

## Naming Convention

Use kebab-case for investigation topics: `eager-loading`, `strategy-pattern`, `channel-based`

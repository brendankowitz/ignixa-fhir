# Reject Investigation

Mark an investigation as rejected with documented rationale.

**Usage**: `/fn-reject {feature-name} {investigation-topic}`

## Instructions

1. **Read the investigation** at `docs/features/{feature-name}/investigations/{topic}.md`

2. **Update the investigation**:
   - Change `**Status**:` to `Rejected`
   - Fill in the Verdict section with clear rejection rationale

3. **Common rejection reasons** (be specific):
   - Layer violation: "Violates architectural layering (e.g., Data layer calling API layer)"
   - Complexity: "Adds 500+ lines for marginal benefit"
   - Performance: "O(n^2) scaling with resource count"
   - DevEx violation: "Requires external service to run locally"
   - Spec non-compliance: "Violates specification"
   - Superseded: "Approach B solves this more elegantly"

4. **Update `readme.md`**: Change investigation status in table to "Rejected"

5. **Output**: Summarize what was rejected and why. Note remaining viable investigations.

## Rejection Format

In the Verdict section:
```markdown
## Verdict
**Rejected**: {One sentence reason}

{Optional: 2-3 sentences of additional context if the rejection isn't obvious}
```

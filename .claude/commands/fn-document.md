# Update Documentation

Trigger the `@documentation-agent` to update project documentation based on recent code changes.

**Usage**: `/fn-document [scope]`

## Instructions

1. **Assess Changes**:
   - Identify recent code changes (commits, modified files).
   - Determine impact on existing documentation (API references, feature guides, architecture docs).

2. **Update Documentation**:
   - Delegate to `@documentation-agent`.
   - Update `README.md` if high-level features changed.
   - Update specific feature docs in `docs/`.
   - Update architecture diagrams or descriptions if designs changed.

3. **Verify**:
   - Ensure the documentation site (if applicable) builds.
   - Verify links and references are valid.
   - Ensure consistent style and tone.

## Scopes
- `feature`: Focus on a specific feature folder.
- `api`: Focus on API reference updates.
- `global`: Review and update project-level docs (README, Architecture).

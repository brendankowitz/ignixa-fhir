# Technical PR Review

Conduct a detailed technical review of recent changes or a specific feature, focusing on logic, bugs, code style, and performance. Research false positives and apply fixes for critical and high-priority items.

**Usage**: `/fn-review [scope]`

## Instructions

1. **Analyze Changes**:
   - Use **GitHub CLI (`gh`)** or **MCP servers** if available to fetch details.
   - Identify new or modified code.
   - If PR exists address comments and conversation history.
   - IF PR exists review builds and checks to ensure they are passing.
   - Review for logic errors, edge cases, and potential bugs.
   - Evaluate against project coding standards and style guides.
   - Check for potential regressions or performance bottlenecks.

2. **Handle Findings**:
   - **Research False Positives**: Verify if flagged issues are genuine bugs or intentional implementations.
   - **Prioritize**: Categorize findings into Critical, High, and Medium.
   - **Next steps**: Always provide actionable next steps.

3. **Remediate**:
   - **Fix Criticals**: Automatically attempt to fix verified "Critical" and "High" priority technical issues (delegate to `@coding-agent`,  `@fast-coding-agent` or `@complex-coding-agent`).
   - **Comment**: Provide feedback on "Medium" or stylistic items.

4. **Verify**:
   - Ensure the build passes after fixes.
   - Verify that tests cover the new logic.

## Scopes
- (default): Review changes in the current working branch/directory.
- `{feature-name}`: Review code within a specific feature folder.

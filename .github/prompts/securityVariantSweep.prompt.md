---
mode: 'agent'
description: 'Audit the current branch for authorization and tenant-isolation gaps of a named pattern, or for all patterns in the security review rules'

---

# Security Variant Sweep

Your goal is to find places in the existing code where a caller can read, change, or delete data beyond the interaction, resource type, compartment, and tenant they were authorized for. Pull request review only covers changed lines. This sweep covers code that no pull request is touching.

Run it after every confirmed security fix, to look for the same pattern elsewhere, and periodically against `main`.

## Input

Either a pattern to hunt for, such as a recently fixed defect ("job re-runs a search more broadly than the kickoff authorized"), or nothing, in which case cover every numbered rule in the security instruction files.

## Rules to apply

Read these files first. They define the invariant and the patterns:

- `.github/instructions/fhir-security.instructions.md`
- `.github/instructions/security-endpoints-authorization.instructions.md`
- `.github/instructions/security-search.instructions.md`
- `.github/instructions/security-background-operations.instructions.md`
- `.github/instructions/security-bundles-writes.instructions.md`

## Method

1. List the entry points for the pattern: every route and its `FhirAuthorizationMetadata`, every MCP tool, every GraphQL field, and every job kickoff, status, and cancel handler.
2. For each one, compare what was authorized (interaction, resource type, tenant) with everything the handler can read, return, count, change, or delete. Write down the caller, the input they control, the check that runs, and the check that is missing.
3. Compare each path with the ordinary path for the same data, with the other storage provider, with the other FHIR versions, and with single-tenant and multi-tenant mode.
4. Try to disprove each candidate. Look for a check in a filter, an explicit `IFhirAuthorizationService` call, or the data layer that already covers it. Drop candidates that are covered.
5. For each candidate that survives, write a test with synthetic data through an authorization-enabled host: one caller that must succeed and one that must be refused. Run it. A candidate without a failing test is a lead, not a finding.

## Rules of engagement

- Use synthetic data and local test fixtures only. Do not probe a deployed service or use real patient data or credentials.
- Do not open a public issue, pull request, or comment that describes an unfixed flaw. Report findings to the maintainer who ran the sweep, for a private security advisory.

## Output

For each finding: the file and line, the caller and the grant they need, the request, what they gain, the test that demonstrates it, and the rule it falls under. Then list the leads you could not confirm and what evidence is missing. State which providers, FHIR versions, and tenancy modes you covered and which you did not.

## After the sweep

If no existing rule would have led a reviewer to a confirmed finding, add or sharpen one in the matching instruction file. Describe the pattern, not the exploit.

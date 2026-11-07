---
applyTo: "src/**,test/**,tools/**,codegen/**,deploy/**,scripts/**,eng/**,build/**,.github/**,*"
excludeAgent: "cloud-agent"
---

# FHIR Security Review Guidelines

Supplement `AGENTS.md` and `REVIEW.md` when reviewing affected security boundaries. These are Ignixa's engineering requirements, not an external certification. Trace the actual principal, input, control, resource, and outcome; distinguish a demonstrated defect from an unverified concern. Do not infer a vulnerability from a historical fix in another implementation.

## Authorization survives every path

- When authentication configuration or token handling changes, check signature/signing-key, issuer, audience, and lifetime validation, including defaults and optional settings that disable checks. Authorization claims must come from validated identity or trusted server context; caller-supplied headers, route values, or query parameters are resource selectors, not proof of permission. Distinguish intentional development/test settings from production behavior.
- Check authentication and authorization separately. Missing, malformed, or ambiguous identity, tenant, patient, or scope context must not broaden permissions on a path that requires it. Examine default configuration as well as explicitly enabled strict modes; respect intentionally anonymous endpoints.
- Preserve tenant isolation and tenant `0`'s system-only boundary. Check route resolution, storage selection, caches, and job state for cross-context access; authentication alone is not tenant authorization. Unqualified routes are valid in single-tenant mode; require `/tenant/{id}` in multi-tenant mode as specified in `AGENTS.md`.
- For SMART/compartment access, check each returned, counted, included, or modified resource against the applicable principal, patient/compartment, resource type, action, and scope constraints. Preserve documented shared-resource exceptions; do not replace them with blanket denial.
- Trace `_include`, `_revinclude`, `:iterate`, wildcard scopes with search constraints, nested chains, and `_has` through every expression branch and expansion. A primary-query filter must not be lost on derived candidates. Check unauthorized existence/count inference as well as returned resource content.
- Check that continuation and `$includes` requests preserve applicable filters and re-establish authorization for the current caller. A continuation token or cached result is not authority to bypass current permissions; consider changed-principal, tenant, and scope attempts.
- Follow authorization through bundles, conditional operations, history, bulk jobs, queues, and retries. Check kickoff, execution, status, cancellation, and result retrieval where applicable; neither job IDs nor a privileged worker identity grant caller access.
- Preserve PATCH protection for `id`, `meta.versionId`, and `meta.lastUpdated`, including alternate paths and bundle execution.

## Inputs, outputs, and resource boundaries

- Keep attacker-controlled values separate from SQL, shell, and other executable syntax. Validate dynamic identifiers and operators against supported choices; parameterizing values alone does not make arbitrary query structure safe.
- Check outbound URL policy at the actual connection boundary, including redirects and name resolution. User-controlled import, package, callback, or reference URLs must not reach destinations forbidden by the service's policy. Do not assume deployment egress controls exist when they are not evidenced.
- Review file/archive extraction for path containment after normalization, links, and bounded expansion. Review parsers, regexes, FHIRPath, nested searches, and bundles for bounded size, depth, time, and total work, including cancellation.
- Keep health data, credentials, bearer tokens, connection strings, and signed URLs out of logs, errors, traces, and test artifacts. Check output encoding/sanitization where narratives or user data reach HTML or another interpreting consumer, and verify de-identification behavior when changed.
- For dependency, build, and deployment changes, inspect source provenance, permissions, secrets, and newly weakened scan/test controls. Never recommend executing untrusted PR code with privileged credentials or treating model output as authorization.

## Evidence that protects the boundary

- Prefer synthetic two-principal or two-patient cases with both permitted and forbidden resources. Include a successful authorized control so denying everything cannot pass as correct.
- Verify tests reach the real authorization path: mocked handlers or authorization-disabled HTTP fixtures cannot prove end-to-end enforcement. Test later pages and derived operations when those paths change, not only the initial response.
- Identify the versions, providers, and configurations actually exercised; do not generalize one R4/SQL test to all supported variants. Inspect skipped cases and weakened assertions instead of relying on total test counts.
- For a new guard, seek evidence that removing the relevant control makes it fail; for a claimed regression, use the base-fails/head-passes standard in `AGENTS.md`. Recommend bounded, isolated checks using synthetic data, never live-service probes or real patient records.

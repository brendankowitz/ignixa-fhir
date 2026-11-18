---
applyTo: "**"
excludeAgent: "cloud-agent"
---

# FHIR Security Review: Core Rules

These rules apply to every pull request. The other `security-*.instructions.md` files in this directory add rules for the paths they match. `AGENTS.md` and `REVIEW.md` still apply. These are Ignixa's engineering requirements, not an external certification.

## How authorization works here

`FhirAuthorizationFilter` authorizes each request once, at the endpoint. It builds a `FhirAuthorizationContext` from the route and the endpoint's `FhirAuthorizationMetadata` (one interaction on one resource type, or `*`), then runs the handler pipeline: authentication, tenant isolation, RBAC, SMART scopes. Application handlers and the data layer do not check again.

So the invariant is: **a request must not read, return, count, change, or delete anything beyond the interaction, resource type, and tenant it was authorized for.** Anything further needs its own call to `IFhirAuthorizationService`, as `BulkDeleteKickoffAuthorizer` does.

## Flag these changes

1. **Work beyond the declared interaction.** A handler that touches a resource type or performs an action the endpoint's `FhirAuthorizationMetadata` did not declare. Examples are `_include` and `_revinclude` targets, bundle entries, conditional lookups, cascades, and reference rewrites.
2. **A new way in.** A new endpoint, MCP tool, GraphQL field, or job kickoff that reaches resource data for a caller. It needs `RequireFhirAuthorization()` with the right metadata, or an equivalent explicit authorization call.
3. **A restricted grant served unrestricted.** A SMART grant with a compartment or search constraint (`FhirAuthorizationFilter.RestrictsData`) is refused today because no query path applies it. A change that accepts such a grant must apply the restriction on every path that returns or changes data, including includes, paging, history, and jobs.
4. **Crossing tenants.** A repository, search service, cache, job, or file path chosen from caller input instead of the resolved `TenantContext`. Tenant `0` is for system operations and must never be reachable through a route. Unqualified routes are valid in single-tenant mode. Multi-tenant mode requires `/tenant/{id}` routes.
5. **Trust taken from the request.** Authority must come from the validated token and server state. A header, route value, query parameter, body field, or continuation token selects a resource. It is not proof of permission.
6. **Enforcement behind a switch.** A new or changed option that relaxes a check, such as `AuthorizationOptions.Enabled`, `RequireAuthentication`, `EnforceTenantIsolation`, or `EnforceCapabilities`. The default must enforce.
7. **Absent context read as allowed.** When the grant being used requires a piece of context, such as the patient for a `patient/` scope or the tenant in multi-tenant mode, a missing or malformed value must refuse the request. Do not require context the grant does not use: `SmartContext` is null for role-based callers, and a successful result may carry no filter.
8. **One provider or version only.** A check added for SQL Server and not FileSystem, or for one of STU3, R4, R4B, and R5, where the other also supports the feature.
9. **Failures turned into success.** A `catch` or fallback that turns an authorization or tenant failure into an empty result, a default, or a broader query.

## Do not flag

- Work the endpoint's declared interaction and resource type already cover. Do not ask handlers or the data layer to authorize again.
- Work with no caller: startup, package preload, TTL cleanup, the transaction watcher, and server-wide jobs such as reindex. They run with system authority by design.
- Missing null checks, `try`/`catch` blocks, or guards for states the types and control flow exclude. `AGENTS.md` and `REVIEW.md` ask for boundary validation, not redundant guards in trusted code.
- A missing best practice, a guessed deployment, or a fix in another FHIR server, without a traced path in this revision.

## Inputs, outputs, and limits

- Keep untrusted values out of SQL, shell, and other executable syntax. Validate dynamic identifiers and operators. Parameterized values do not make a caller-chosen query structure safe.
- For outbound calls (import sources, package feeds, callbacks, references), check the destination at connection time, including redirects and name resolution.
- For file and archive extraction, check path containment after normalization, links, and bounded expansion.
- Bound the size, depth, time, and total work of parsers, regular expressions, FHIRPath, nested searches, bundles, and jobs.
- Keep health data, tokens, connection strings, and signed URLs out of logs, errors, traces, and test artifacts. Check encoding where narrative or user data reaches HTML, and check de-identification behavior when it changes.
- For build, dependency, and workflow changes, check provenance, permissions, secrets, and any weakened scan or test step.

## Writing the finding

- State the caller and the grant they hold, the request they send, and what they get that they should not. If you cannot trace that, ask a question instead of asserting a vulnerability.
- Suggest the smallest fix that uses an existing mechanism: the right `FhirAuthorizationMetadata`, an explicit `IFhirAuthorizationService` call modelled on `BulkDeleteKickoffAuthorizer`, or `RequireServerWideAuthority()`. Do not propose a new layer or a duplicate check.
- A new check needs a test that fails without it. For a claimed regression, use the base-fails and head-passes standard in `AGENTS.md`, and inspect skipped cases and weakened assertions. Use two principals, two patients, or two tenants: one request that must succeed and one that must be refused.
- Tests with mocked authorization or `AuthorizationOptions.Enabled = false` do not prove enforcement. Say which provider, FHIR version, and tenancy mode the tests exercise. When paging or a derived operation changes, ask for a test of a later page or the derived path, not only the first response.
- Recommend only bounded, isolated checks with synthetic data. Never suggest probing a live service, using real patient records, running untrusted pull request code with privileged credentials, or treating model output as authorization.

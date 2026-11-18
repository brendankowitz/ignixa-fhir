---
applyTo: "src/Application/Ignixa.Application.BackgroundOperations/**,src/Application/Ignixa.Application.Operations/**,src/Application/Ignixa.Application/Features/BulkDelete/**,src/Application/Ignixa.Application/Features/Export/**,src/Application/Ignixa.Api/Endpoints/BulkDelete*,src/Application/Ignixa.Api/Endpoints/ExportEndpoints.cs,src/Application/Ignixa.Api/Endpoints/ImportEndpoints.cs,src/Application/Ignixa.Api/Endpoints/ReindexEndpoints.cs"
excludeAgent: "cloud-agent"
---

# FHIR Security Review: Bulk and Background Operations

Export, import, reindex, bulk delete, and other jobs are authorized at kickoff and run later with the worker's identity. The worker has no caller, so the job does whatever its definition says.

## Kickoff

1. **Authorize what the job will do.** The route filter authorizes one interaction on the route's type. A job can reach further. The kickoff must authorize every interaction on every type the job can touch, as `BulkDeleteKickoffAuthorizer` does for deletes, cascades through `_include` and `_revinclude`, and reference rewrites. When the type set cannot be known, require the `*` grant.
2. **Refuse grants the job cannot honor.** A grant with a compartment or search constraint (`RestrictsData`) must be refused unless the job definition stores the restriction and every activity applies it.
3. **Options only narrow.** An option that excludes types or resources must not be used to decide what authority is needed. An option that widens the job must be part of the authorization.
4. **The definition is trusted state.** The tenant, the criteria, and anything that carries authority in the job definition must be set by server code from the authorized request. Request input must not set them directly.

## Execution

5. **Stay in the tenant.** Every activity must use the tenant stored in the job. Flag an activity that resolves storage from ambient state, defaults to tenant `0`, or iterates tenants when the kickoff was for one.
6. **Act on what was authorized.** If an activity re-runs a search, it must produce the set the kickoff authorized. Flag criteria that are re-parsed more broadly at execution time.
7. **Retries, resumes, and child jobs** must keep the tenant and the restrictions.
8. **Report honestly.** Skipped or failed resources must not be counted as processed.

## Status, cancel, and results

9. **A job ID is not a credential.** Status, cancel, and result routes must check the caller's authority for that operation, confirm the job belongs to the caller's tenant, and confirm it is the expected job type. This applies to the MCP job tools too.
10. **Result links are credentials.** Issue signed URLs only after authorization, scope them to the job's output, and keep them out of logs.
11. **Outbound sources and destinations.** Import sources, package feeds, and export destinations must follow the configured policy, checked at connection time including redirects.

## Tests to ask for

- A caller with the grant and a caller without it submit the same job. Assert what was changed or exported, not only the job status.
- A job that reaches a second type through `_include` or `_revinclude`.
- Status and cancel calls from another tenant and from a caller without the operation's permission.

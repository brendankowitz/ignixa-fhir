# ADR-2610: Custom Audit Headers (X-IGNIXA-AUDIT-*, AHDS-Compatible)

**Status**: Proposed
**Date**: 2026-10-06
**Feature**: custom-audit-headers

## Context

Callers of Azure Health Data Services (AHDS) can attach their own audit fields with `X-MS-AZUREFHIR-AUDIT-*`
request headers. Existing AHDS-compatible clients depend on this: for example, they send `X-MS-AZUREFHIR-AUDIT-BUNDLEID`
and `-OPERATIONID` when ingesting bundles and `-OPERATIONID` when reading data, and rely on AHDS AuditLogs
for its audit trail. Before this change, Ignixa's `FhirAuditFilter` ignored these headers. Its `$export` and `$import`
endpoints were not audited at all.

Reference behavior, verified in microsoft/fhir-server and healthcare-shared-components:

- `KnownHeaders.CustomAuditHeaderPrefix = "X-MS-AZUREFHIR-AUDIT-"`. `AuditHeaderReader` matches the prefix case-insensitively,
  keeps header names as received, and comma-joins multi-valued headers. It checks each value's length first,
  then the header count. The result is cached in `HttpContext.Items`.
- `AuditConstants`: at most 10 headers; at most 2048 characters per value.
- `AuditHeaderTooLargeException` and `AuditHeaderCountExceededException` map to **431** with an OperationOutcome
  (`error` / `invalid`). Messages contain the header name and lengths, never the value.
- Headers are read only for audited requests, before the action runs. The local `AuditLogger` writes
  `CustomHeaders: name=value;...`.
- fhir-server does **not** copy custom headers onto bundle inner-entry requests. Background jobs (import,
  bulk update and delete) log with `customHeaders: null`.

## Options Considered

0. **Header prefix.** (a) Accept only the AHDS prefix `X-MS-AZUREFHIR-AUDIT-`. (b) Accept only an Ignixa prefix
   `X-IGNIXA-AUDIT-`. That would break existing AHDS-compatible clients, which hard-code the AHDS prefix. (c) Use `X-IGNIXA-AUDIT-` as Ignixa's
   own prefix and keep accepting `X-MS-AZUREFHIR-AUDIT-` as a compatibility alias.
1. **Sidecar contract shape.** (a) Add a new `map<string,string> custom_headers = 13` field. (b) Fold the headers into
   the existing `custom_properties` map. Option (b) collides with Ignixa keys (`action`, `path`, ...) and loses
   the AHDS distinction between custom headers and server properties.
2. **Bundle entries.** (a) Fhir-server parity: only the outer request carries the headers. (b) Copy the outer request's
   audit headers onto each entry request, which `BundleEntryExecutor` already does for `X-TTL`.
3. **Async jobs.** (a) Parity: audit only the HTTP kick-off, status, and cancel requests. (b) Also persist the kick-off
   attribution on the job definition and emit one audit event when the job reaches a terminal status.

## Decision

- Prefix: **Option 0c**. `X-IGNIXA-AUDIT-*` is the documented prefix; `X-MS-AZUREFHIR-AUDIT-*` is accepted for AHDS clients.
  Both share one 10-header limit, and names are recorded as received, so records from AHDS-compatible clients keep their AHDS header names.
- Add `CustomAuditHeaders` (Application) to read and validate headers with fhir-server's limits, order of checks,
  and messages. Violations raise `AuditHeaderTooLargeException` / `AuditHeaderCountExceededException`
  (`FhirException`, 431, `invalid`).
- `FhirAuditFilter` validates before the handler runs. A violation is audited as a 431 record without the headers and is then
  rethrown to `FhirExceptionMiddleware`. Valid headers go onto `HttpRequestAuditEvent.CustomHeaders`. The audited status is
  the one the caller receives: a thrown `FhirException`'s status, or the status of a returned `IResult`.
- Sidecar: **Option 1a**. This is additive, so existing sidecars ignore the new field. The local structured log
  writes `CustomHeaders=name=value;...` ordered by name, with control characters replaced.
- Bundles: **Option 2b**. Each entry's audit record carries the bundle request's headers.
- Async jobs: **Option 3b**. `$export` and `$import` endpoints now use `FhirAuditFilter`. `BackgroundJobAuditContext`
  (user, correlation ID, headers) is persisted on `ExportJobDefinition` / `ImportJobDefinition`, from both the HTTP endpoints and
  the MCP job tools. `BackgroundJobCompletionAuditor` emits `IAuditLogger.LogBackgroundJobCompleted` after a *successful*
  terminal update in `CompleteJobActivity`, `GetJobStatusHandler` reconciliation, or the cancel endpoints. Repositories reject
  updates to an already-terminal job, so only the first terminal writer emits. The auditor never throws: the terminal status is
  already committed, so an audit failure is logged with the job's identity.

Options 2b and 3b go beyond fhir-server. Option 2b ensures that a caller's operation ID can be found on the audit record of every
resource it touched, not only on the bundle request. Option 3b ensures that the outcome of an asynchronous operation can be
attributed to its caller.

## Consequences

- Caller-supplied operation and bundle IDs appear on every audit record that AHDS would produce. They also appear on
  per-entry and job-completion records, which AHDS does not produce.
- `$export` and `$import` requests are now audited and enforce the header limits. A request with invalid headers
  is rejected with 431 before any job is created.
- Existing audit data changes shape: a request whose handler throws a `FhirException` (404, 412, ...) is now recorded
  with that status and outcome `4`, rather than outcome `8` with the response's provisional status. Returned `IResult`
  statuses are recorded as well.
- Header values are persisted, inside the job definition JSON, for the life of a job record. Status endpoints and
  MCP tools project specific definition fields and do not return the audit context.
- Jobs created before this change have no audit context. Their completion events are attributed to
  `unknown` and carry no headers, and a warning is logged.
- The completion event is at most once. If the job's runtime fails before it records its own outcome, the event is emitted
  by the next status poll, and never if nobody polls. A crash between the terminal update and the audit call loses the
  event.
- Audit delivery remains fire-and-forget and is **not durable**. Sidecar failures are logged and the event is dropped.
  A durable outbox is a separate gap.
- Older server versions running side by side drop the unknown `AuditContext` field when they re-save a job definition,
  so that job completes without attribution.

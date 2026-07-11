# Investigation: Subscription Callback Verification

**Feature**: testscript
**Status**: In Progress
**Created**: 2026-07-11

## Approach

FHIR TestScript is pure request/response — there is no spec primitive for "wait for an inbound
notification" (confirmed against the actual model, not just spec reading: `OperationExpression` and
`AssertExpression` in `Ignixa.TestScript` are entirely outbound-request-shaped; zero
callback/webhook/notification hits anywhere in `src/Core/Ignixa.TestScript`). Testing rest-hook
subscription delivery needs something outside that model entirely, on two tracks:

**Track A — mock rest-hook receiver as a TestScript fixture.** Add a new fixture provider,
`ITestCallbackReceiver`, that spins up an in-process HTTP listener (Kestrel `WebApplication` or a
lightweight `HttpListener`, matching the DI-injected-provider style `IFixtureProvider`/
`ITestRequestProvider` already use) exposing a base URL a TestScript can reference as a
`Subscription.channel.endpoint`. It records every inbound POST (body + headers) keyed by a
correlation value (e.g. the subscription id or a header the test sets).

**Track B — a new assertion extension to query it.** `http://ignixa.io/testscript/assertionCallbackReceived`,
reusing the polling primitive from [async-job-polling](async-job-polling.md): poll the receiver's captured
requests for one matching a FHIRPath predicate (e.g. `header.value = '${subscriptionId}'`) within
`maxAttempts * intervalMs`, pass/fail like any other assertion. Unlike Track A's job-status polling this
polls in-memory state, not HTTP, so it's cheaper and doesn't need the raw-JSON-vs-FHIRPath problem noted
in that investigation — the captured body is a real FHIR resource (the delivered payload) and can go
through the normal `element.ToElement(schemaProvider)` path.

**Longer term**, once Subscriptions is actually implemented (`ISubscriptionChannel`/`RestHookChannel` per
the existing design docs), the cleaner seam is having the test harness register an in-process channel
implementation directly — no real HTTP round-trip needed — mirroring how `ITestRequestProvider` already
supports in-process execution for the FHIR server itself. That's strictly better than Track A once it's
available, but Track A is what's buildable *today* since subscriptions delivery doesn't exist yet.

## Tradeoffs

| Pros | Cons |
|------|------|
| Track A/B are independently useful even without Subscriptions being built — any future feature that does outbound webhook-style delivery gets a reusable test receiver | Building `ITestCallbackReceiver` now is pure speculation: Subscriptions has no ADR yet (status is "Investigation Complete", explicitly "no ADR yet"), so there's real risk designing a receiver against an `ISubscriptionChannel` shape that doesn't exist yet |
| `assertionCallbackReceived` reuses the polling mechanism from the async-job investigation rather than inventing a second one | This is two new pieces of infrastructure (a receiver + an assertion extension) for a feature that's still pre-implementation — against this project's own YAGNI guidance, this is arguably premature |
| The in-process-channel version (long-term option) avoids real network sockets in tests entirely, which is faster and avoids port-binding flakiness in CI | No existing mock-webhook test infra to build on at all — `grep -r "WireMock"` = 0 hits, the only `HttpListener` usage in the whole repo is an unrelated CLI report viewer |
| Keeps callback testing inside the same TestScript authoring model developers already use for CRUD tests | Even with a receiver + assertion built, this only tests *that Ignixa POSTed something* — verifying content correctness, retry/backoff behavior, and channel error handling likely still needs targeted C# integration tests, not TestScript |

## Alignment

- [ ] Follows architectural layering rules — `ITestCallbackReceiver` would live in `Ignixa.TestScript` (Core), fine, but it has no consumer yet since Subscriptions isn't implemented — can't fully validate the layering until there's a real channel to plug into
- [x] Developer Experience — if built, authoring a subscription-delivery test would look like any other TestScript test
- [ ] Specification compliance — same as the async-job investigation, this is 100% a custom Ignixa extension; TestScript has no spec answer here at all
- [ ] Consistent with existing patterns — partially: the assertion-extension half matches ADR 2607 precedent, but the receiver-fixture half has no existing analog to compare against

## Evidence

- Subscriptions status: `docs/features/subscriptions/readme.md:3,27` — "Investigation Complete", "No ADR yet - implementation planning based on research findings." Design docs (`subscription-engine.md`, `transaction-table.md`, ~1900 lines combined) are illustrative pseudocode referencing a legacy Microsoft implementation and `fhir-candle`, not Ignixa source — `grep -r "Subscription"` under `src/` only hits generated FHIR model/schema files, no channel/sender classes exist.
- Roadmap placement: `subscription-engine.md:1165` targets "Phase 23"; `transaction-table.md:850-866` targets "Phase 7/12" — well beyond current repo state.
- No mock webhook receiver infra exists: only `HttpListener` usage repo-wide is `test/Ignixa.Tests.Compatibility.CLI/TestResultsViewerCommand.cs:65,94,111,121`, an HTML test-report viewer, unrelated. Zero `WireMock` references.
- Confirmed no callback primitive in the TestScript model itself: `src/Core/Ignixa.TestScript/Expressions/OperationExpression.cs` fields are entirely outbound (`Type`, `Url`, `Params`, `Method`, `Headers`); `AssertExpression`/`AssertCriteria`/`AssertOperator` only assert against the last in-context response. `grep` for callback/webhook/notification/inbound across `src/Core/Ignixa.TestScript` = 0 hits.
- No Subscriptions ADR: `docs/adr/*.md` (23 files) has none named `*subscription*`. The investigation doc header references "Original ADR: 2600" (`subscription-engine.md:6`) but `adr-2600-*.md` doesn't exist — reserved/placeholder number only. Closest implemented ADR is `adr-2607-testscript-extensions.md`, which documents today's extension points (`parametrize`, `fhirVersions`, `requiresCapability`, fhirfakes generation) — none touch async/callback testing.

## Verdict

*Pending evaluation — recommend deferring implementation.* The assertion-extension half (Track B) is
low-risk and reuses established patterns, but the receiver half (Track A) is speculative against an
unimplemented feature. Suggest holding this investigation as design input for whenever the Subscriptions
ADR is actually written, rather than building `ITestCallbackReceiver` now — build it once
`ISubscriptionChannel`'s real shape is known, so the receiver is designed against the actual interface
instead of a guess.

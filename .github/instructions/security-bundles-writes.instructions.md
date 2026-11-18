---
applyTo: "src/Application/Ignixa.Application/Features/Bundle/**,src/Application/Ignixa.Application/Features/ConditionalOperations/**,src/Application/Ignixa.Application/Features/Patch/**,src/Application/Ignixa.Application/Features/Resource/**,src/Application/Ignixa.Api/Endpoints/FhirEndpoints.cs,src/Application/Ignixa.Api/Endpoints/PatchEndpoints.cs"
excludeAgent: "cloud-agent"
---

# FHIR Security Review: Bundles, Conditional Operations, and Writes

A bundle, a conditional operation, or a reference can make the server search or write on the caller's behalf. The route was authorized once. Each derived action needs the caller's authority just as a direct request would.

## Bundles

1. **Each entry is its own request.** The bundle route is authorized as a transaction or batch on `*`. Entries are dispatched through their own endpoints' pipelines with the caller's principal, so `FhirAuthorizationFilter` runs for each one. Flag a change that executes an entry any other way, and check that a refused entry writes nothing.
2. **Work before entry authorization.** Flag work in `BundleReferencePreProcessor`, `BundleProcessor`, or `BundleEntryExecutor` that reads stored data or resolves references before the entry is authorized.
3. **Entries stay in the tenant.** An entry URL, a `fullUrl`, or a reference must not select another tenant or an absolute URL on another server.
4. **Deferred and parallel execution.** `DeferredWriteCoordinator` and channel execution must not let an entry run with another entry's context, or commit writes for entries that were refused.

## Conditional operations and references

5. **A conditional operation performs a search.** Conditional create, update, patch, and delete, and conditional references, look up resources. The caller needs search authority on the type being searched as well as the write.
6. **Outcomes disclose data.** Zero, one, or many matches, resolved IDs, and distinct errors can reveal that a resource exists. Check what a caller without read access learns.
7. **Deletes that expand.** A conditional delete that removes more than one resource or follows includes needs delete authority on every type it removes.

## Writes

8. **Protected fields.** PATCH must not change `id`, `meta.versionId`, or `meta.lastUpdated`, through any path syntax, alternate operation type, or bundle entry. Update must not change `resourceType` or `id`.
9. **Validate what is stored.** Validation that ran before a patch, a reference rewrite, or a merge must still hold for the resource that is written.
10. **Secondary writes.** A header or option that creates or changes another resource needs the interaction on that resource too.
11. **Merge contract.** Keep the application-level visibility transaction of `MergeResources` as `AGENTS.md` describes. Do not recommend wrapping it in a SQL transaction.

## Tests to ask for

- A caller with a write grant and no search grant sends the operation directly and inside a transaction. Both must be treated the same.
- A read-only caller posts a transaction that writes. Assert the entry is refused and nothing is stored.

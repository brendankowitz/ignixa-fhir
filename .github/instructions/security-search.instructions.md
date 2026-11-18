---
applyTo: "src/Core/Ignixa.Search/**,src/Core/Ignixa.Search.Sql/**,src/Application/Ignixa.Application/Features/Search/**,src/Application/Ignixa.Application/Features/Compartment/**,src/Application/Ignixa.Application/Features/History/**,src/Application/Ignixa.Application/Features/Resource/**,src/Application/Ignixa.Application/Features/Experimental/GraphQl/**,src/Application/Ignixa.Application/Features/Experimental/Mcp/Tools/**,src/Application/Ignixa.Api/Endpoints/**,src/Application/Ignixa.Application.Operations/**,src/Application/Ignixa.Application/Infrastructure/**,src/Application/Ignixa.Application/Features/Conformance/**,src/Application/Ignixa.Application/Features/Packages/**,src/Application/Ignixa.Application/Features/Admin/**,src/Application/Ignixa.Application/Features/Specification/**,src/Application/Ignixa.Application.BackgroundOperations/Conformance/**,src/Application/Ignixa.Domain/**,src/DataLayer/**"
excludeAgent: "cloud-agent"
---

# FHIR Security Review: Search and Storage

A search request is authorized for one interaction on one resource type in one tenant. The query can still return more than that through expansion, paging, or a different execution path.

## Expansion

1. **Included types need authority too.** `_include`, `_revinclude`, `:iterate`, chaining, `_has`, and `$everything` return or test resources of types the route did not name. Check that the caller holds read or search on each type that can be returned, not only on the route's type.
2. **Existence leaks.** Totals, `next` links, and distinct errors or status codes must not reveal resources the caller cannot read. A chained or `_has` predicate on a forbidden type is an oracle even when nothing of that type is returned.
3. **Compartments.** `CompartmentSearchExpression`, the compartment lowering rules, and the compartment definitions decide membership. A change that widens membership, or that treats an unresolved or foreign reference as a member, widens access. Keep documented shared-resource exceptions. `HiddenCompartmentMembership` does not decide membership. It reports membership parameters the tenant cannot search yet, so that an incomplete result carries a warning. Keep that warning.

## Paging and history

4. **Continuation tokens are caller input.** `ContinuationToken`, `IncludesContinuationToken`, and `KeysetContinuationToken` can be crafted or replayed by another caller or in another tenant. The route filter authorizes each page for the current caller. Check that the handler does not take its tenant, resource type, or filter from the token alone.
5. **History and version reads.** Authorize the versions that are returned, in the caller's tenant. A check on the current version does not cover a historical or deleted one.

## Execution paths and providers

6. **Alternate paths must agree.** A new query plan, lowering rule, cache, in-memory index, or fast path that a caller's request can reach must return the same rows the ordinary path would allow. Flag a path that skips a predicate the ordinary path applies.
7. **SQL Server and FileSystem must agree.** A restriction enforced in one provider needs the matching change, or a stated reason, in the other.
8. **Tenant scoping in storage.** SQL Server isolates tenants by database, so the control is which connection is selected. Check that the repository or search service comes from `IFhirRepositoryFactory` or `ISearchServiceFactory` for the resolved tenant. Do not ask for a tenant predicate on tables that have no tenant column. Ask for one only where storage shares rows, files, or cache entries across tenants.
9. **Definitions are policy inputs.** Search parameter and compartment definitions can be shared across tenants. A change one tenant can make must not alter what another tenant's searches match.
10. **Lossy matching.** Truncated, case-folded, or approximate index values are candidates. Where a predicate enforces a boundary, the final decision must use the exact value. Normalization that only affects ordinary search matching is not a finding.

## Query construction

11. **Structure is not a parameter.** Sort columns, operators, table names, and search parameter codes that reach SQL text must come from a fixed set. Parameterized values do not cover them.
12. **Bounded work.** Check depth and count limits on chains, includes, `:iterate`, and result size, and that cancellation is honored.

## Tests to ask for

- Two tenants and two patients with synthetic data. Assert the body, the count, and the links.
- The first page and a later page, and a token replayed by a different caller.
- Both providers and the FHIR versions the change affects.

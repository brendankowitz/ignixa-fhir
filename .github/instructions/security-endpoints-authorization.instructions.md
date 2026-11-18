---
applyTo: "src/Application/Ignixa.Api/**,src/Application/Ignixa.Api.OpenIddict/**,src/Application/Ignixa.Application/Features/Authorization/**,src/Application/Ignixa.Application/Features/Experimental/**"
excludeAgent: "cloud-agent"
---

# FHIR Security Review: Endpoints, Scopes, and Identity

These files decide who the caller is and what one request is allowed to do. Nothing later in the request checks again, so a mistake here applies to every handler behind it.

## Endpoints

1. **Every route is authorized or deliberately public.** A new route needs `RequireFhirAuthorization()`. Only discovery and liveness routes belong in `PublicRoutes` in `EndpointAuthorizationCoverageTests`. Flag any addition to that list that returns or changes resource data.
2. **The declared interaction must match the work.** Check `FhirAuthorizationMetadata` against what the handler does. A route declared as `Read` or `SearchSystem` must not create, update, or delete. If the handler can do more than the route declares, it must authorize each further interaction explicitly or refuse it, as `GraphQlEndpoints.RefuseMutation` refuses mutations while authorization is enabled. A route that can touch every type must declare `*`.
3. **Operations have no permission of their own.** Each `$operation` route must declare the interaction it authorizes as. Read, search, and write permissions are independent, so an operation that performs more than one interaction needs authority for each of them.
4. **Server-wide effects need server-wide authority.** An operation that changes state shared by all tenants needs `RequireServerWideAuthority()` after `RequireFhirAuthorization()`.
5. **Filter order.** `TenantResolutionMiddleware` must run before authorization. A filter or middleware that returns early must not let a request reach a handler unauthorized.
6. **Other entry points.** MCP tools must authorize through `IMcpAuthorizationService`, and a tool that acts on tenant data derives from `TenantAwareMcpTool`. GraphQL routes are authorized as a search on `*`, and mutations are refused while authorization is enabled because they have no per-resource check. A change that lets mutations run must authorize each one for the interaction and type it performs. Check that an IDE or debug surface does not run mutations unauthenticated.

## The handler pipeline

7. **Order and fail-fast.** Handlers run by `Priority`: authentication, tenant isolation, RBAC, SMART. A new handler, a changed priority, or a handler that returns success early must not skip a later denial.
8. **Roles.** A change to `DefaultRoles`, `Permissions`, `McpEnabledRoles`, or `SystemAdminRole` handling changes what existing identities can do. Ask who gains access.
9. **Sidecar failure.** When the authorization sidecar is unavailable the request must fail. It must not fall back to allow.
10. **Filters merge to the narrower result.** `FhirAuthorizationFilter.Merge` combines restrictions from several handlers. A merge must not drop a restriction or replace it with a wider one.

## Scopes and tokens

11. **Unknown must refuse.** In `SmartScopeParser`, a scope that is malformed, unsupported, or carries a constraint the server cannot enforce must be rejected. It must not be dropped or granted without its constraint.
12. **Split before decoding.** A decoded value must not introduce a new separator, resource type, or constraint.
13. **Keep the scope kind.** A `patient/` or `user/` scope must not be treated as a broader kind. Check v1 compatibility (`EnableV1ScopeCompatibility`) and v2 syntax separately.
14. **Token validation.** Check signature, signing keys, issuer, audience, and lifetime, and any setting that disables them. Where a grant depends on launch context or `fhirUser`, check how the value is bound to a resource in this service and this tenant.
15. **Advertised and enforced must match.** The capability statement and `.well-known/smart-configuration` must not advertise a scope or capability the server refuses or ignores.

## Tests to ask for

- A token with the grant and a token without it, sent through an authorization-enabled host as `SmartAuthorizationFailClosedTests` does.
- For a new route, the updated endpoint census and a request with no credentials.

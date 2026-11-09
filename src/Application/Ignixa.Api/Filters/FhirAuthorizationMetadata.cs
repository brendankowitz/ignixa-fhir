using Ignixa.Application.Features.Authorization.Models;

namespace Ignixa.Api.Filters;

/// <summary>
/// The interaction an endpoint authorizes as, in place of the one <see cref="FhirAuthorizationFilter"/> derives from
/// the route. Every operation route declares one, because an operation has no SMART permission of its own.
/// </summary>
/// <param name="Interaction">The interaction the endpoint performs.</param>
/// <param name="ResourceType">The resource type it acts on, <c>*</c> for every type, or null for the route's own type.</param>
public sealed record FhirAuthorizationMetadata(FhirInteraction Interaction, string? ResourceType = null);

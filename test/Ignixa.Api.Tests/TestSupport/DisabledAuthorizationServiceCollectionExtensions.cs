using Ignixa.Application.Features.Authorization;
using Ignixa.Application.Features.Authorization.Services;
using Ignixa.Application.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Ignixa.Api.Tests.TestSupport;

internal static class DisabledAuthorizationServiceCollectionExtensions
{
    /// <summary>
    /// Registers what <c>FhirAuthorizationFilter</c> needs to be constructed, with authorization disabled, for hosts
    /// that test an endpoint's behaviour rather than its authorization.
    /// </summary>
    public static IServiceCollection AddDisabledFhirAuthorization(this IServiceCollection services) =>
        services
            .AddSingleton(Substitute.For<IFhirAuthorizationService>())
            .AddSingleton(Substitute.For<IFhirRequestContextAccessor>())
            .AddSingleton(Options.Create(new AuthorizationOptions { Enabled = false }));
}

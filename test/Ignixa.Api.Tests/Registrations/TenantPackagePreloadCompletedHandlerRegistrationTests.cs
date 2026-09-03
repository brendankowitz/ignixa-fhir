// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Autofac.Extensions.DependencyInjection;
using Ignixa.Application.Events.Startup;
using Medino;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ignixa.Api.Tests.Registrations;

/// <summary>
/// Pins the wiring rule that made <c>SqlReferenceDataPreloadHandler</c> dead code for its whole life, so
/// the next notification handler registered the same way is caught rather than shipped inert.
/// <para>
/// That handler was registered with <c>services.AddSingleton&lt;SqlReferenceDataPreloadHandler&gt;()</c> --
/// service type only. Autofac's <c>Populate</c> registers <c>descriptor.ServiceType</c> and nothing else:
/// it performs no interface discovery, so the concrete type resolves and
/// <c>INotificationHandler&lt;T&gt;</c> does not. Medino's <c>PublishAsync</c> dispatches by resolving
/// <c>IEnumerable&lt;INotificationHandler&lt;T&gt;&gt;</c>, which was therefore always empty, and the
/// handler never executed once. It has since been deleted; this test keeps the mechanism covered with a
/// stand-in.
/// </para>
/// </summary>
public class TenantPackagePreloadCompletedHandlerRegistrationTests
{
    private sealed class FakePreloadHandler : INotificationHandler<TenantPackagePreloadCompletedEvent>
    {
        public Task HandleAsync(TenantPackagePreloadCompletedEvent notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    [Fact]
    public void GivenAHandlerRegisteredByConcreteTypeOnly_WhenResolvingHandlersForTheEvent_ThenNoneAreReturned()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<FakePreloadHandler>();

        var builder = new ContainerBuilder();
        builder.Populate(services);
        using var container = builder.Build();

        // Act
        var handlers = container.Resolve<IEnumerable<INotificationHandler<TenantPackagePreloadCompletedEvent>>>();

        // Assert -- the concrete type resolves, the notification interface does not.
        container.IsRegistered<FakePreloadHandler>().ShouldBeTrue();
        handlers.ShouldBeEmpty();
    }

    [Fact]
    public void GivenAHandlerRegisteredAsTheNotificationInterface_WhenResolvingHandlersForTheEvent_ThenItIsReturned()
    {
        // Arrange
        var builder = new ContainerBuilder();
        builder.RegisterType<FakePreloadHandler>()
            .As<INotificationHandler<TenantPackagePreloadCompletedEvent>>()
            .InstancePerDependency();
        using var container = builder.Build();

        // Act
        var handlers = container.Resolve<IEnumerable<INotificationHandler<TenantPackagePreloadCompletedEvent>>>();

        // Assert -- proves the empty result above is the registration's fault, not the resolution's.
        handlers.ShouldHaveSingleItem().ShouldBeOfType<FakePreloadHandler>();
    }
}

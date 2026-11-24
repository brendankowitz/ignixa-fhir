// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Abstractions;
using Ignixa.Api.Registrations;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Validation.Abstractions;
using Medino;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Registrations;

/// <summary>
/// Pins the Autofac optional-constructor-parameter mechanism <c>CreateOrUpdateResourceHandler</c> and
/// <c>BundleProcessor</c> rely on for their optional <c>SemanticIndexer?</c> dependency (the same pattern
/// already used by <c>IMcpAuthorizationService?</c> on <c>TenantAwareMcpTool</c>): when
/// <c>VectorSearch:Enabled</c> is false, <c>AddSemanticSearch</c> registers nothing for
/// <c>SemanticIndexer</c>, so it must never be registered in this container either -- and resolving a
/// handler that declares it as an optional constructor parameter (default <c>null</c>) must still
/// succeed, receiving <c>null</c>, rather than throwing <see cref="Autofac.Core.DependencyResolutionException"/>.
/// </summary>
public class SemanticIndexerOptionalResolutionTests
{
    [Fact]
    public void GivenSemanticIndexerNotRegistered_WhenResolvingWriteHandler_ThenResolutionSucceeds()
    {
        var builder = new ContainerBuilder();
        builder.RegisterApplicationServices(new ConfigurationBuilder().Build());
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>));
        builder.RegisterInstance(Substitute.For<IFhirVersionContext>()).As<IFhirVersionContext>();
        builder.RegisterInstance(Substitute.For<IFhirRequestContextAccessor>()).As<IFhirRequestContextAccessor>();
        builder.RegisterInstance(Substitute.For<IPartitionStrategy>()).As<IPartitionStrategy>();
        builder.RegisterInstance(Substitute.For<IFhirRepositoryFactory>()).As<IFhirRepositoryFactory>();
        builder.RegisterInstance<Func<FhirVersion, IValidationSchemaResolver>>(_ => Substitute.For<IValidationSchemaResolver>());

        // Intentionally does NOT register SemanticIndexer -- this is what AddSemanticSearch does (or
        // rather does not do) when VectorSearch:Enabled is false.
        using var container = builder.Build();

        var handler = container.Resolve<IRequestHandler<CreateOrUpdateResourceCommand, UpdateResult>>();

        handler.ShouldBeOfType<CreateOrUpdateResourceHandler>();
    }
}

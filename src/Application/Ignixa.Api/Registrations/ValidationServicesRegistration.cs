// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.DataLayer.SqlEntityFramework.Features.Terminology;
using Ignixa.Domain.Abstractions;
using Ignixa.FhirPath.Parser;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Services;

namespace Ignixa.Api.Registrations;

/// <summary>
/// Registers validation services including schema resolvers, terminology services,
/// and validation infrastructure.
/// </summary>
public static class ValidationServicesRegistration
{
    /// <summary>
    /// Registers validation services in the Autofac container.
    /// </summary>
    public static ContainerBuilder RegisterValidationServices(this ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Validation schema builder
        builder.Register(c =>
        {
            var compiler = c.Resolve<FhirPathParser>();
            return new StructureDefinitionSchemaBuilder(compiler);
        })
        .AsSelf()
        .SingleInstance();

        // Package resource provider
        builder.RegisterType<PackageResourceProvider>()
            .As<IPackageResourceProvider>()
            .SingleInstance();

        // Validation schema resolver factory (version + tenant aware)
        RegisterValidationSchemaResolverFactory(builder);

        // Terminology services
        RegisterTerminologyServices(builder);

        // Conformance resource services
        RegisterConformanceServices(builder);

        return builder;
    }

    private static void RegisterValidationSchemaResolverFactory(ContainerBuilder builder)
    {
        // Multi-tenant factory: Func<FhirVersion, int, IValidationSchemaResolver>
        builder.Register<Func<FhirVersion, int, IValidationSchemaResolver>>(c =>
        {
            var versionContext = c.Resolve<IFhirVersionContext>();
            var schemaBuilder = c.Resolve<StructureDefinitionSchemaBuilder>();
            var terminologyService = c.Resolve<ITerminologyService>();

            return (version, tenantId) =>
            {
                var schemaProvider = versionContext.GetSchemaProvider(version, tenantId);
                var resolver = new StructureDefinitionSchemaResolver(schemaProvider, schemaBuilder, terminologyService);
                return new CachedValidationSchemaResolver(resolver);
            };
        }).SingleInstance();

        // Single-tenant factory (backward compatibility, defaults to tenant 1)
        builder.Register<Func<FhirVersion, IValidationSchemaResolver>>(c =>
        {
            var multiTenantFactory = c.Resolve<Func<FhirVersion, int, IValidationSchemaResolver>>();
            return version => multiTenantFactory(version, 1);
        }).SingleInstance();
    }

    private static void RegisterTerminologyServices(ContainerBuilder builder)
    {
        // InMemoryTerminologyService (fallback for non-imported terminology)
        builder.Register<InMemoryTerminologyService>(c =>
        {
            var requestContext = c.Resolve<IFhirRequestContextAccessor>().RequestContext;
            var fhirVersion = requestContext?.FhirVersion ?? FhirVersion.R4;
            return new InMemoryTerminologyService(fhirVersion);
        })
        .AsSelf()
        .InstancePerLifetimeScope();

        // SqlTerminologyService (database-backed terminology)
        builder.RegisterType<SqlTerminologyService>()
            .AsSelf()
            .InstancePerLifetimeScope();

        // HybridTerminologyService (routes to SQL or fallback based on import status)
        builder.Register<ITerminologyService>(c =>
        {
            var sqlService = c.Resolve<SqlTerminologyService>();
            var fallbackService = c.Resolve<InMemoryTerminologyService>();
            var logger = c.Resolve<ILogger<HybridTerminologyService>>();

            return new HybridTerminologyService(sqlService, fallbackService, logger);
        }).InstancePerLifetimeScope();
    }

    private static void RegisterConformanceServices(ContainerBuilder builder)
    {
        // In-memory conformance cache
        builder.RegisterType<Ignixa.Domain.Caching.InMemoryConformanceCache>()
            .As<Ignixa.Domain.Caching.IFhirConformanceCache>()
            .SingleInstance();

        // Conformance resource resolver
        builder.RegisterType<Ignixa.Domain.Caching.ConformanceResourceResolver>()
            .As<IConformanceResourceResolver>()
            .SingleInstance();
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.DataLayer.SqlServer;
using Ignixa.DataLayer.SqlServer.Features.Terminology;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Terminology;
using Ignixa.FhirPath.Parser;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.Specification;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Services;
using Ignixa.Validation.Services.Terminology;

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
            var logger = c.Resolve<ILogger<StructureDefinitionSchemaBuilder>>();
            return new StructureDefinitionSchemaBuilder(compiler, logger);
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
        // The returned resolver is a ProfileAwareValidationSchemaResolver so that
        // consumers with access to the resource element can call ResolveForElement()
        // for meta.profile composition, while legacy callers that look up by canonical
        // URL via IValidationSchemaResolver.GetSchema() still work (delegated to the
        // inner cached resolver).
        builder.Register<Func<FhirVersion, int, IValidationSchemaResolver>>(c =>
        {
            var versionContext = c.Resolve<IFhirVersionContext>();
            var schemaBuilder = c.Resolve<StructureDefinitionSchemaBuilder>();
            var terminologyService = c.Resolve<ITerminologyService>();

            return (version, tenantId) =>
            {
                var schemaProvider = versionContext.GetSchemaProvider(version, tenantId);
                var resolver = new StructureDefinitionSchemaResolver(schemaProvider, schemaBuilder, terminologyService);
                var cached = new CachedValidationSchemaResolver(resolver);
                return new ProfileAwareValidationSchemaResolver(cached);
            };
        }).InstancePerLifetimeScope();

        // Single-tenant factory (backward compatibility, defaults to tenant 1)
        builder.Register<Func<FhirVersion, IValidationSchemaResolver>>(c =>
        {
            var multiTenantFactory = c.Resolve<Func<FhirVersion, int, IValidationSchemaResolver>>();
            return version => multiTenantFactory(version, 1);
        }).InstancePerLifetimeScope();
    }

    private static void RegisterTerminologyServices(ContainerBuilder builder)
    {
        // Package content for the request's FHIR version and tenant. Needs a valid request context.
        builder.Register(c =>
        {
            var versionContext = c.Resolve<IFhirVersionContext>();
            var requestContext = c.Resolve<IFhirRequestContextAccessor>().RequestContext
                ?? throw new InvalidOperationException(
                    "FHIR request context not available. PackageTerminologySource requires " +
                    "a valid request context with FhirVersion and TenantId for terminology resolution.");
            var schemaProvider = versionContext.GetSchemaProvider(requestContext.FhirVersion, requestContext.TenantId);
            return new PackageTerminologySource(schemaProvider.ValueSetProvider);
        })
        .AsSelf()
        .InstancePerLifetimeScope();

        // SqlServerTerminologyService (database-backed terminology, raw ADO.NET).
        // SystemPartitionId because terminology is server-wide rather than per-tenant: the tables it reads
        // live in the system partition's database, not the caller's. Mutable results are not process-cached.
        builder.Register(c => new SqlServerTerminologyService(
                c.Resolve<ISqlExecutionService>(),
                SystemConstants.SystemPartitionId,
                c.Resolve<ILogger<SqlServerTerminologyService>>()))
            .AsSelf()
            .As<ITerminologyImportStatusProvider>()
            .InstancePerLifetimeScope();

        // Source order is routing order: SQL is authoritative for imported content, then package content.
        builder.Register<ITerminologyService>(c => new ComposingTerminologyService(
            [c.Resolve<SqlServerTerminologyService>(), c.Resolve<PackageTerminologySource>()]))
            .InstancePerLifetimeScope();
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

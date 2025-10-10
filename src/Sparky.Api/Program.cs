// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Autofac.Extensions.DependencyInjection;
using Medino;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IO;
using Sparky.Api.Infrastructure;
using Sparky.Api.Middleware;
using Sparky.Api.Services;
using System;
using Sparky.Domain.Abstractions;
using Sparky.DataLayer.FileSystem.FileSystem;
using Sparky.DataLayer.InMemoryIndex;
using Sparky.Application.Features.Bundle;
using Sparky.Application.Features.Bundle.Serialization;
using Sparky.Application.Features.Resource;
using Sparky.Search.Parsing;
using Sparky.Search.Expressions.Parsers;
using Sparky.Search.Definition;
using Sparky.Search.Indexing.SearchValues;
using Sparky.Extensions.Schema;
using Sparky.Specification.Schema;
using Sparky.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Configure Autofac as the service provider factory
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Register RecyclableMemoryStreamManager as singleton
builder.Services.AddSingleton<RecyclableMemoryStreamManager>();

// Register IHttpContextFactory and IHttpContextAccessor for bundle entry pipeline routing
builder.Services.AddSingleton<IHttpContextFactory, DefaultHttpContextFactory>();
builder.Services.AddHttpContextAccessor();

// Register IndexLoaderService as hosted service
builder.Services.AddHostedService<IndexLoaderService>();

// Configure Autofac container
builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
{
    // Register InMemoryResourceLocationIndex
    containerBuilder.RegisterType<InMemoryResourceLocationIndex>()
        .As<IResourceLocationIndex>()
        .SingleInstance();

    // Register FileBasedFhirRepository
    string baseDirectory = builder.Configuration["FhirRepository:BaseDirectory"]
        ?? Path.Combine(Directory.GetCurrentDirectory(), "fhir-data");

    containerBuilder.Register(c =>
    {
        var logger = c.Resolve<ILogger<FileBasedFhirRepository>>();
        var memoryStreamManager = c.Resolve<RecyclableMemoryStreamManager>();
        return new FileBasedFhirRepository(baseDirectory, logger, memoryStreamManager);
    }).As<IFhirRepository>().AsSelf().SingleInstance();

    // Register Medino service provider
    containerBuilder.Register<IMediatorServiceProvider>(c =>
    {
        var context = c.Resolve<IComponentContext>();
        return new AutofacMediatorServiceProvider(context);
    }).SingleInstance();

    // Register Medino mediator
    containerBuilder.RegisterType<Mediator>().As<IMediator>().SingleInstance();

    // Generic resource handlers (replaces Patient-specific handlers)
    containerBuilder.RegisterType<GetResourceHandler>()
        .As<IRequestHandler<GetResourceQuery, Sparky.Domain.Models.ResourceWrapper?>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<CreateOrUpdateResourceHandler>()
        .As<IRequestHandler<CreateOrUpdateResourceCommand, Sparky.Domain.Models.ResourceKey>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<DeleteResourceHandler>()
        .As<IRequestHandler<DeleteResourceCommand, bool>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<SearchResourcesHandler>()
        .As<IRequestHandler<SearchResourcesQuery, SearchResourcesResult>>()
        .InstancePerDependency();

    // Register search services
    containerBuilder.Register(c =>
    {
        var repository = c.Resolve<IFhirRepository>();
        var logger = c.Resolve<ILogger<FileBasedSearchService>>();
        return new FileBasedSearchService(repository, logger, baseDirectory);
    }).As<ISearchService>().SingleInstance();

    // Register query parameter parser
    containerBuilder.RegisterType<QueryParameterParser>()
        .As<IQueryParameterParser>()
        .InstancePerDependency();

    // Register search options builder (requires ExpressionParser)
    containerBuilder.RegisterType<SearchOptionsBuilder>()
        .As<ISearchOptionsBuilder>()
        .InstancePerDependency();

    // Register FhirVersionContext (provides version-specific schema providers and search indexers)
    // Similar to HAPI FHIR's FhirContext pattern - caches instances per FHIR version
    containerBuilder.RegisterType<Sparky.Application.Infrastructure.FhirVersionContext>()
        .As<Sparky.Application.Infrastructure.IFhirVersionContext>()
        .SingleInstance();

    // Register ExpressionParser dependencies (still needed for search query parsing)
    containerBuilder.Register(c =>
    {
        return new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R4);
    }).As<IFhirSchemaProvider>().SingleInstance();

    containerBuilder.RegisterType<SearchParameterDefinitionManager>()
        .As<ISearchParameterDefinitionManager>()
        .SingleInstance();

    containerBuilder.Register<ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver>(c =>
    {
        var manager = c.Resolve<ISearchParameterDefinitionManager>();
        return () => manager;
    }).SingleInstance();

    // Register ReferenceSearchValueParser (required by SearchParameterExpressionParser)
    containerBuilder.RegisterType<ReferenceSearchValueParser>()
        .As<IReferenceSearchValueParser>()
        .InstancePerDependency();

    containerBuilder.RegisterType<SearchParameterExpressionParser>()
        .As<ISearchParameterExpressionParser>()
        .InstancePerDependency();

    containerBuilder.RegisterType<ExpressionParser>()
        .As<IExpressionParser>()
        .InstancePerDependency();

    // Register bundle processing services
    containerBuilder.RegisterType<BundleReferencePreProcessor>()
        .InstancePerDependency();

    containerBuilder.RegisterType<BundleEntryExecutor>()
        .InstancePerDependency();

    containerBuilder.RegisterType<BundleChannelExecutor>()
        .InstancePerDependency();

    containerBuilder.RegisterType<BundleResponseBuilder>()
        .InstancePerDependency();

    containerBuilder.RegisterType<BundleProcessor>()
        .InstancePerDependency();

    // Register StreamingBundleParser for Prefer: streaming header support
    containerBuilder.RegisterType<StreamingBundleParser>()
        .InstancePerDependency();

    // Register pipeline executor for bundle entry routing
    // Uses ASP.NET Core endpoint routing infrastructure (similar to microsoft/fhir-server BundleRouter)
    containerBuilder.Register(c =>
    {
        var endpointDataSource = c.Resolve<EndpointDataSource>();
        var matcherPolicies = c.Resolve<IEnumerable<Microsoft.AspNetCore.Routing.MatcherPolicy>>();
        var endpointSelector = c.Resolve<Microsoft.AspNetCore.Routing.Matching.EndpointSelector>();
        var templateBinderFactory = c.Resolve<Microsoft.AspNetCore.Routing.Template.TemplateBinderFactory>();
        return new AspNetCorePipelineExecutor(endpointDataSource, matcherPolicies, endpointSelector, templateBinderFactory);
    })
    .As<Sparky.Application.Infrastructure.IPipelineExecutor>()
    .SingleInstance();
});

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseFhirExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapFhirEndpoints();
app.MapControllers(); // Keep for MetadataController

app.Logger.LogInformation("FHIR Server v2 starting...");
app.Logger.LogInformation("FHIR data directory: {BaseDirectory}",
    builder.Configuration["FhirRepository:BaseDirectory"]
    ?? Path.Combine(Directory.GetCurrentDirectory(), "fhir-data"));

app.Run();

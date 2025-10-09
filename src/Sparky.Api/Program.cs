// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Autofac.Extensions.DependencyInjection;
using Medino;
using Microsoft.IO;
using Sparky.Api.Infrastructure;
using Sparky.Api.Middleware;
using Sparky.Api.Services;
using Sparky.Domain.Abstractions;
using Sparky.DataLayer.FileSystem.FileSystem;
using Sparky.DataLayer.InMemoryIndex;
using Sparky.Application.Features.Patient;
using Sparky.Search.Parsing;
using Sparky.Search.Expressions.Parsers;
using Sparky.Search.Definition;
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

    // Register all handlers from the Application assembly
    containerBuilder.RegisterType<CreateOrUpdatePatientHandler>()
        .As<IRequestHandler<CreateOrUpdatePatientCommand, Sparky.Domain.Models.ResourceKey>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<GetPatientHandler>()
        .As<IRequestHandler<GetPatientQuery, Sparky.Domain.Models.ResourceWrapper?>>()
        .InstancePerDependency();

    containerBuilder.RegisterType<SearchPatientHandler>()
        .As<IRequestHandler<SearchPatientQuery, SearchPatientResult>>()
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

    // Register ExpressionParser dependencies
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

    containerBuilder.RegisterType<SearchParameterExpressionParser>()
        .As<ISearchParameterExpressionParser>()
        .InstancePerDependency();

    containerBuilder.RegisterType<ExpressionParser>()
        .As<IExpressionParser>()
        .InstancePerDependency();
});

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseFhirExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Logger.LogInformation("FHIR Server v2 starting...");
app.Logger.LogInformation("FHIR data directory: {BaseDirectory}",
    builder.Configuration["FhirRepository:BaseDirectory"]
    ?? Path.Combine(Directory.GetCurrentDirectory(), "fhir-data"));

app.Run();

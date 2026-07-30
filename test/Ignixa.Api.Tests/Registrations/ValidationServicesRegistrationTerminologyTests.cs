// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Api.Registrations;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.DataLayer.SqlServer;
using Ignixa.DataLayer.SqlServer.Features.Terminology;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Terminology;
using Ignixa.Validation.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Api.Tests.Registrations;

/// <summary>
/// Proves the composition root actually serves terminology from the ported SQL Server implementation.
/// <para>
/// The container built here registers only what the terminology graph needs -- notably <b>no</b>
/// <c>SqlEntityFrameworkRepositoryFactory</c>, which the EF <c>SqlTerminologyService</c> required. Resolution
/// succeeding at all is therefore structural evidence that the EF path is gone, not merely unused.
/// </para>
/// </summary>
public class ValidationServicesRegistrationTerminologyTests
{
    private readonly ISqlExecutionService _sqlExecutionService = Substitute.For<ISqlExecutionService>();

    /// <summary>
    /// Every terminology query funnels through <see cref="ISqlExecutionService"/>. Returning no rows for the
    /// system lookup is the shortest path that still proves the query was issued.
    /// </summary>
    private IContainer BuildContainer()
    {
        _sqlExecutionService.ExecuteReaderAsync(
                Arg.Any<int>(),
                Arg.Any<SqlCommand>(),
                Arg.Any<Func<SqlDataReader, int>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<int>>([]));

        var requestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        requestContextAccessor.RequestContext.Returns(Substitute.For<IFhirRequestContext>());

        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterInstance(NullLoggerFactory.Instance).As<ILoggerFactory>();
        builder.RegisterInstance(_sqlExecutionService).As<ISqlExecutionService>();
        builder.RegisterInstance(requestContextAccessor).As<IFhirRequestContextAccessor>();

        // Recursive substitution supplies the schema provider and its ValueSetProvider; the fallback service
        // is constructed but never reached by the assertions below.
        builder.RegisterInstance(Substitute.For<IFhirVersionContext>()).As<IFhirVersionContext>();
        builder.RegisterInstance(new MemoryCache(new MemoryCacheOptions())).As<IMemoryCache>();

        builder.RegisterValidationServices();

        return builder.Build();
    }

    [Fact]
    public void GivenTheValidationServiceRegistrations_WhenResolvingTerminology_ThenTheHybridServiceIsReturned()
    {
        // Arrange
        using var container = BuildContainer();
        using var scope = container.BeginLifetimeScope();

        // Act
        var terminologyService = scope.Resolve<ITerminologyService>();

        // Assert
        terminologyService.ShouldBeOfType<HybridTerminologyService>();
    }

    [Fact]
    public void GivenTheValidationServiceRegistrations_WhenResolvingTheImportStatusProvider_ThenItIsTheSqlServerService()
    {
        // Arrange
        using var container = BuildContainer();
        using var scope = container.BeginLifetimeScope();

        // Act
        var statusProvider = scope.Resolve<ITerminologyImportStatusProvider>();

        // Assert
        statusProvider.ShouldBeOfType<SqlServerTerminologyService>();

        // The hybrid receives this same per-scope instance for both its SQL side and its routing decision.
        statusProvider.ShouldBeSameAs(scope.Resolve<SqlServerTerminologyService>());
    }

    /// <summary>
    /// The EF <c>SqlTerminologyService</c> must not be reachable. Asserting on the registration rather than
    /// on the resolved graph's private fields is what makes this a wiring test instead of a reflection test.
    /// </summary>
    [Fact]
    public void GivenTheValidationServiceRegistrations_WhenInspectingTheContainer_ThenTheEfTerminologyServiceIsNotRegistered()
    {
        // Arrange
        using var container = BuildContainer();

        // Act
        var efIsRegistered = container
            .IsRegistered<Ignixa.DataLayer.SqlEntityFramework.Features.Terminology.SqlTerminologyService>();

        // Assert
        efIsRegistered.ShouldBeFalse();
    }

    /// <summary>
    /// $translate routes to the SQL side unconditionally, so it reaches the hybrid's SQL dependency without
    /// an import-status query standing in the way. The resolved graph issuing a raw ADO.NET command against
    /// the system partition is the behavioural proof that it is the ported service: the EF implementation
    /// went through a <c>FhirDbContext</c> and never touched <see cref="ISqlExecutionService"/>.
    /// </summary>
    [Fact]
    public async Task GivenTheResolvedTerminologyService_WhenTranslating_ThenTheSqlServerImplementationQueriesTheSystemPartition()
    {
        // Arrange
        using var container = BuildContainer();
        using var scope = container.BeginLifetimeScope();
        var terminologyService = scope.Resolve<ITerminologyService>();

        var parameters = new TranslateParameters(
            Url: null,
            ConceptMapVersion: null,
            Code: "abc",
            System: "http://example.org/cs",
            Version: null,
            Source: null,
            Target: null,
            TargetSystem: null);

        // Act
        var result = await terminologyService.TranslateCodeAsync(parameters, CancellationToken.None);

        // Assert
        result.Result.ShouldBeFalse();
        result.Message.ShouldBe("Source system 'http://example.org/cs' not found");

        await _sqlExecutionService.Received(1).ExecuteReaderAsync(
            SystemConstants.SystemPartitionId,
            Arg.Any<SqlCommand>(),
            Arg.Any<Func<SqlDataReader, int>>(),
            Arg.Any<CancellationToken>());
    }
}

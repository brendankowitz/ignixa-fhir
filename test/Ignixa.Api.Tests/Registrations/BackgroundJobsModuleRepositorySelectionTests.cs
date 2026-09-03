// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Api.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.Api.Tests.Registrations;

/// <summary>
/// Tests that <see cref="BackgroundJobsModule"/> correctly selects the repository implementation
/// based on configuration, logs the selection, and fails fast on typos.
/// </summary>
/// <remarks>
/// Previously, a typo in the BackgroundJobs:Repository setting (e.g., "SqlSever" instead of "SqlServer")
/// would silently fall back to in-memory storage with no diagnostic message. This meant a deployed server
/// would lose job state on every restart, with no evidence in the logs of why. This test suite ensures
/// that: (1) the correct repository is selected for each valid input, (2) typos and other unrecognized
/// non-empty values throw immediately with a clear error message, and (3) absent/empty values use the
/// documented default (in-memory).
/// </remarks>
public sealed class BackgroundJobsModuleRepositorySelectionTests
{
    [Fact]
    public void GivenAbsentBackgroundJobsRepositoryKey_WhenLoadingTheModule_ThenInMemoryRepositoryIsRegisteredAndNoThrowOccurs()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterInstance(NullLoggerFactory.Instance).As<ILoggerFactory>();

        // Act & Assert: no throw should occur
        builder.RegisterModule(new BackgroundJobsModule(configuration));

        using var container = builder.Build();

        // The in-memory repository should be registered and resolvable
        var repository = container.Resolve<IBackgroundJobRepository<ExportJobDefinition>>();
        repository.ShouldNotBeNull();
        repository.GetType().Name.ShouldContain("InMemory");
    }

    [Fact]
    public void GivenEmptyBackgroundJobsRepositoryKey_WhenLoadingTheModule_ThenInMemoryRepositoryIsRegisteredAndNoThrowOccurs()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "BackgroundJobs:Repository", "" },
            })
            .Build();

        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterInstance(NullLoggerFactory.Instance).As<ILoggerFactory>();

        // Act & Assert: no throw should occur
        builder.RegisterModule(new BackgroundJobsModule(configuration));

        using var container = builder.Build();

        // The in-memory repository should be registered and resolvable
        var repository = container.Resolve<IBackgroundJobRepository<ExportJobDefinition>>();
        repository.ShouldNotBeNull();
        repository.GetType().Name.ShouldContain("InMemory");
    }

    [Fact]
    public void GivenSqlServerBackgroundJobsRepository_WhenLoadingTheModule_ThenSqlServerRepositoryIsRegisteredAndNoThrowOccurs()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "BackgroundJobs:Repository", "SqlServer" },
            })
            .Build();

        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterInstance(NullLoggerFactory.Instance).As<ILoggerFactory>();

        // Act & Assert: no throw should occur
        builder.RegisterModule(new BackgroundJobsModule(configuration));

        using var container = builder.Build();

        // The SQL Server repository should be registered and resolvable
        var repository = container.Resolve<IBackgroundJobRepository<ExportJobDefinition>>();
        repository.ShouldNotBeNull();
        repository.GetType().Name.ShouldContain("SqlServer");
    }

    [Fact]
    public void GivenSqlserverLowercaseBackgroundJobsRepository_WhenLoadingTheModule_ThenSqlServerRepositoryIsRegisteredAndNoThrowOccurs()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "BackgroundJobs:Repository", "sqlserver" },
            })
            .Build();

        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterInstance(NullLoggerFactory.Instance).As<ILoggerFactory>();

        // Act & Assert: no throw should occur
        builder.RegisterModule(new BackgroundJobsModule(configuration));

        using var container = builder.Build();

        // The SQL Server repository should be registered and resolvable (case-insensitive)
        var repository = container.Resolve<IBackgroundJobRepository<ExportJobDefinition>>();
        repository.ShouldNotBeNull();
        repository.GetType().Name.ShouldContain("SqlServer");
    }

    [Fact]
    public void GivenTypoInBackgroundJobsRepository_WhenLoadingTheModule_ThenThrowsWithMessageNamingTheBadValue()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "BackgroundJobs:Repository", "SqlSever" },  // Typo: "SqlSever" not "SqlServer"
            })
            .Build();

        var builder = new ContainerBuilder();

        // Act & Assert: InvalidOperationException should be thrown
        var exception = Should.Throw<InvalidOperationException>(() =>
            builder.RegisterModule(new BackgroundJobsModule(configuration)));

        exception.Message.ShouldContain("SqlSever");
        exception.Message.ShouldContain("Unrecognized");
        exception.Message.ShouldContain("SqlServer");
    }

    [Fact]
    public void GivenInvalidRepositoryValue_WhenLoadingTheModule_ThenErrorMessageIncludesAcceptedValues()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "BackgroundJobs:Repository", "InvalidRepo" },
            })
            .Build();

        var builder = new ContainerBuilder();

        // Act & Assert
        var exception = Should.Throw<InvalidOperationException>(() =>
            builder.RegisterModule(new BackgroundJobsModule(configuration)));

        exception.Message.ShouldContain("InvalidRepo");
        exception.Message.ShouldContain("SqlServer");
        exception.Message.ShouldContain("empty");
    }
}

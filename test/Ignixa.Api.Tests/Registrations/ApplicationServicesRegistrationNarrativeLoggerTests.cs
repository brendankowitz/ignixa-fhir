// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Abstractions;
using Ignixa.Api.Registrations;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.NarrativeGenerator;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Ignixa.Api.Tests.Registrations;

/// <summary>
/// Pins that resolving <see cref="INarrativeGenerator"/> actually asks the container for an
/// <see cref="ILoggerFactory"/> and threads a real logger into <c>FhirNarrativeGenerator.Create</c>.
/// </summary>
/// <remarks>
/// Before this fix, <c>RegisterNarrativeServices</c> called <c>FhirNarrativeGenerator.Create(schema)</c>
/// with no logger argument, so <c>FhirPathScriptFunctions</c>'s five Warning-level catch-block logs
/// (see <c>FhirPathScriptFunctionsErrorReportingTests</c> and <c>FhirNarrativeGeneratorLoggingTests</c>
/// in Ignixa.NarrativeGenerator.Tests, which prove the logging itself works once a real logger is
/// supplied) silently defaulted to <c>NullLogger</c> in production no matter how well that logging was
/// unit tested in isolation.
///
/// This registers the identical <c>RegisterNarrativeServices</c> delegate used in
/// <c>RegisterApplicationServices</c> over a minimal container -- just <see cref="IFhirVersionContext"/>,
/// <see cref="IFhirRequestContextAccessor"/>, and <see cref="ILoggerFactory"/> -- rather than standing up
/// the full application registration graph, which pulls in many unrelated dependencies (search indexers,
/// package repositories, etc.) that add noise without changing what this test proves. Mirrors
/// <c>SearchServicesRegistrationValidateTenantHostnamesTests</c>'s minimal-container pattern.
/// </remarks>
public class ApplicationServicesRegistrationNarrativeLoggerTests
{
    [Fact]
    public void GivenTheNarrativeRegistration_WhenResolvingINarrativeGenerator_ThenARealLoggerIsRequestedForFhirNarrativeGenerator()
    {
        // Arrange
        var schema = Substitute.For<IFhirSchemaProvider>();
        var versionContext = Substitute.For<IFhirVersionContext>();
        versionContext.GetBaseSchemaProvider(FhirVersion.R4).Returns(schema);

        var requestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        requestContextAccessor.RequestContext.Returns((IFhirRequestContext?)null);

        var loggerFactory = Substitute.For<ILoggerFactory>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

        var builder = new ContainerBuilder();
        builder.RegisterInstance(versionContext).As<IFhirVersionContext>();
        builder.RegisterInstance(requestContextAccessor).As<IFhirRequestContextAccessor>();
        builder.RegisterInstance(loggerFactory).As<ILoggerFactory>();
        ApplicationServicesRegistration.RegisterNarrativeServices(builder);

        using var container = builder.Build();

        // Act
        var generator = container.Resolve<INarrativeGenerator>();

        // Assert: this is the wiring the earlier fix was missing. Resolving the generator must ask the
        // container's ILoggerFactory for a logger scoped to FhirNarrativeGenerator; before the fix,
        // ILoggerFactory was never even resolved by this registration.
        generator.ShouldNotBeNull();
        loggerFactory.Received(1).CreateLogger(Arg.Is<string>(category => category.Contains("FhirNarrativeGenerator", StringComparison.Ordinal)));
    }
}

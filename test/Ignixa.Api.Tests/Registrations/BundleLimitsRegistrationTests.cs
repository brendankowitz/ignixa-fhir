// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Autofac;
using Ignixa.Api.Registrations;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Domain.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.Api.Tests.Registrations;

public class BundleLimitsRegistrationTests
{
    [Theory]
    [InlineData("Bundle:MaxTransactionEntries", null)]
    [InlineData("Bundle:MaxTransactionEntries", "-1")]
    [InlineData("Bundle:MaxTransactionEntries", "0")]
    [InlineData("Bundle:MaxTransactionEntries", "not-a-number")]
    [InlineData("Kestrel:Limits:MaxRequestBodySize", null)]
    [InlineData("Kestrel:Limits:MaxRequestBodySize", "-1")]
    [InlineData("Kestrel:Limits:MaxRequestBodySize", "0")]
    [InlineData("Kestrel:Limits:MaxRequestBodySize", "not-a-number")]
    public void GivenInvalidBundleLimitConfiguration_WhenRegisteringServices_ThenStartupFails(
        string key,
        string? value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();
        var builder = new ContainerBuilder();

        Should.Throw<InvalidOperationException>(() => builder.RegisterApplicationServices(configuration));
    }

    [Fact]
    public async Task GivenSmallConfiguredBodyLimit_WhenResolvingParser_ThenTokensAboveItsCeilingAreRejected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kestrel:Limits:MaxRequestBodySize"] = "9000"
            })
            .Build();
        var builder = new ContainerBuilder();
        builder.RegisterInstance(NullLogger<StreamingBundleParser>.Instance)
            .As<ILogger<StreamingBundleParser>>();
        builder.RegisterApplicationServices(configuration);
        using var container = builder.Build();
        var parser = container.Resolve<StreamingBundleParser>();
        var context = await parser.ParseStreamAsync(new MemoryStream(Encoding.UTF8.GetBytes(
            """
            {
              "resourceType": "Bundle",
              "type": "batch",
              "entry": [{
                "resource": { "resourceType": "Binary", "data": "
            """ + new string('A', 9_000) + """
            " },
                "request": { "method": "PUT", "url": "Binary/1" }
              }]
            }
            """)));

        await Should.ThrowAsync<RequestNotValidException>(async () =>
        {
            await foreach (var _ in context.Entries)
            {
            }
        });
    }
}

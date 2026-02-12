// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Microsoft.Extensions.DependencyInjection;
using Ignixa.Abstractions;
using Ignixa.Anonymizer.Extensions;

namespace Ignixa.Anonymizer.Tests.Utilities;

public static class AsyncTestHelper
{
    public static IServiceProvider BuildServiceProvider(
        string configFilePath,
        IFhirSchemaProvider schema,
        Action<AnonymizerBuilder>? configureBuilder = null)
    {
        var services = new ServiceCollection();

        services.AddFhirAnonymizer(builder =>
        {
            builder.WithConfigurationFile(configFilePath);
            configureBuilder?.Invoke(builder);
        });

        services.AddSingleton(schema);
        services.AddLogging();

        return services.BuildServiceProvider();
    }

    public static IAnonymizerEngine GetEngine(IServiceProvider provider)
    {
        return provider.GetRequiredService<IAnonymizerEngine>();
    }
}

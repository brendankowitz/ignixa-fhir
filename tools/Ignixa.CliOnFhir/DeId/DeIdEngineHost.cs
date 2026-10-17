// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.DeId;
using Ignixa.DeId.Configuration;
using Ignixa.DeId.Extensions;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ignixa.CliOnFhir.DeId;

/// <summary>
/// An <c>Ignixa.DeId</c> engine for one run, built the way <c>tools/Ignixa.DeId.Cli</c> builds it (<c>AddFhirDeId</c> over a service
/// collection with the schema and a validation schema resolver), from options already checked by <see cref="DeIdConfiguration"/>.
/// Logging is registered with no provider, so the library logs nothing: its messages quote rule paths and the resources it works on,
/// and what a run must report it reports through each resource's result instead.
/// </summary>
internal sealed class DeIdEngineHost : IDisposable
{
    private readonly ServiceProvider _services;

    private DeIdEngineHost(ServiceProvider services, IDeIdEngine engine)
    {
        _services = services;
        Engine = engine;
    }

    /// <summary>The engine.</summary>
    public IDeIdEngine Engine { get; }

    /// <summary>Builds the engine; its processors are created now, so a setting they cannot take fails here, before anything is read.</summary>
    /// <exception cref="ToolRequestException"><c>deid-config-invalid</c>: the library could not be set up with the configuration.</exception>
    public static DeIdEngineHost Create(DeIdOptions options, IFhirSchemaProvider schema)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(schema);

        var services = new ServiceCollection();
        services.AddSingleton(schema);
        services.AddSingleton<IValidationSchemaResolver>(_ => new CachedValidationSchemaResolver(new StructureDefinitionSchemaResolver(schema)));
        services.AddLogging();
        services.AddSingleton(Options.Create(options));
        services.AddFhirDeId();
        ServiceProvider provider = services.BuildServiceProvider();
        try
        {
            return new DeIdEngineHost(provider, provider.GetRequiredService<IDeIdEngine>());
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            provider.Dispose();

            // A processor's constructor refused a setting; its message can quote the setting, which may be a key.
            throw ToolRequestException.Error("deid-config-invalid", $"Ignixa.DeId could not be set up with the configuration ({ex.GetType().Name}); its message is not repeated because it can quote a setting.");
        }
    }

    public void Dispose() => _services.Dispose();
}

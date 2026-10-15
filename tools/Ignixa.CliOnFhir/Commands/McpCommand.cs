// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.CliOnFhir.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Commands;

/// <summary>
/// <c>cli-on-fhir mcp &lt;path&gt;</c>: serves the folder over MCP on stdin/stdout. Stdout carries
/// only protocol messages, so all logging goes to stderr.
/// </summary>
internal static class McpCommand
{
    public static Command Create()
    {
        Argument<string> pathArgument = CommonOptions.CreatePathArgument();
        Option<string> versionOption = CommonOptions.CreateFhirVersionOption();

        Option<string?> cacheDirOption = CommonOptions.CreateCacheDirOption();
        Option<bool> noCacheOption = CommonOptions.CreateNoCacheOption();

        var command = new Command("mcp", "Serve a folder of FHIR data to an agent over MCP (stdio)");
        command.Arguments.Add(pathArgument);
        command.Options.Add(versionOption);
        CommonOptions.AddCacheOptions(command, cacheDirOption, noCacheOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string path = parseResult.GetValue(pathArgument)!;
            string version = parseResult.GetValue(versionOption)!;
            IndexCacheOptions cacheOptions = CommonOptions.ResolveCache(parseResult, cacheDirOption, noCacheOption);

            HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
            builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.Logging.SetMinimumLevel(LogLevel.Warning);

            builder.Services.AddSingleton(services => new FolderSession(
                path,
                FhirSchemas.Resolve(version),
                services.GetRequiredService<ILoggerFactory>(),
                cacheOptions));
            builder.Services
                .AddMcpServer(options => options.ServerInfo = new() { Name = "cli-on-fhir", Version = typeof(McpCommand).Assembly.GetName().Version?.ToString() ?? "0.0.0" })
                .WithStdioServerTransport()
                .WithTools<FolderTools>()
                .WithTools<ResourceTools>()
                .WithTools<PatientTools>();

            await builder.Build().RunAsync(cancellationToken);
            return 0;
        });

        return command;
    }
}

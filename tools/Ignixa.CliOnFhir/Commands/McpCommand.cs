// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
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
        var terminology = new TerminologyCliOptions();
        var maxResponseBytesOption = new Option<int>("--max-response-bytes")
        {
            Description = $"The most one tool response may take, however large a maxBytes a call asks for (default {EnvelopeJson.McpMaxBytes}, "
                + $"at most {EnvelopeJson.MaxBytes}). Agent clients such as Copilot move larger results to a file the model cannot read without a shell",
            DefaultValueFactory = _ => EnvelopeJson.McpMaxBytes,
        };
        maxResponseBytesOption.Validators.Add(result =>
        {
            int value = result.GetValueOrDefault<int>();
            if (value is < EnvelopeJson.MinBudget or > EnvelopeJson.MaxBytes)
            {
                result.AddError($"--max-response-bytes must be from {EnvelopeJson.MinBudget} to {EnvelopeJson.MaxBytes}, not {value}.");
            }
        });

        var command = new Command("mcp", "Serve a folder of FHIR data to an agent over MCP (stdio)");
        command.Arguments.Add(pathArgument);
        command.Options.Add(versionOption);
        command.Options.Add(maxResponseBytesOption);
        CommonOptions.AddCacheOptions(command, cacheDirOption, noCacheOption);
        terminology.AddTo(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string path = parseResult.GetValue(pathArgument)!;
            string version = parseResult.GetValue(versionOption)!;
            IndexCacheOptions cacheOptions = CommonOptions.ResolveCache(parseResult, cacheDirOption, noCacheOption);
            TerminologyOptions terminologyOptions = terminology.Resolve(parseResult, cacheDirOption);
            int maxResponseBytes = parseResult.GetValue(maxResponseBytesOption);

            HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
            builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.Logging.SetMinimumLevel(LogLevel.Warning);

            builder.Services.AddSingleton(services => new FolderSession(
                path,
                FhirSchemas.Resolve(version),
                services.GetRequiredService<ILoggerFactory>(),
                cacheOptions,
                terminology: terminologyOptions,
                maxResponseBytes: maxResponseBytes));
            builder.Services.AddHostedService<SessionWarmup>();
            McpServerSetup.AddTools(builder.Services
                .AddMcpServer(options => McpServerSetup.Configure(options))
                .WithStdioServerTransport());

            await builder.Build().RunAsync(cancellationToken);
            return 0;
        });

        return command;
    }
}

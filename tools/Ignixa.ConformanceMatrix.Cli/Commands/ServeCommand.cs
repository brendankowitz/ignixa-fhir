using System.CommandLine;
using Ignixa.ConformanceMatrix.Cli.Serving;
using Ignixa.Specification.Generated;

namespace Ignixa.ConformanceMatrix.Cli.Commands;

internal static class ServeCommand
{
    public static Command Build()
    {
        var command = new Command("serve", "Host TestScripts as a local load-test runner (Azure Load Testing / Locust sidecar)");

        var testsOption = new Option<string>("--tests") { Description = "Folder containing TestScript .json files (scanned recursively)", Required = true };
        var portOption = new Option<int>("--port") { Description = "TCP port to listen on", DefaultValueFactory = _ => 5599 };
        var hostIpOption = new Option<string>("--host-ip") { Description = "IP address to bind the listener to", DefaultValueFactory = _ => "127.0.0.1" };
        var fhirVersionOption = new Option<string?>("--fhir-version")
        {
            Description = "FHIR version forwarded to every evaluation and the Accept header (e.g. '4.0', '4.3', '5.0'); a per-call fhirVersion in the /run request body overrides this."
        };
        var authHeaderOption = new Option<string?>("--auth-header")
        {
            Description = "Static authentication header applied to FHIR requests (e.g. 'Bearer <token>'). Ignored when FHIR_TOKEN_URL configures client-credentials token auth."
        };

        command.Options.Add(testsOption);
        command.Options.Add(portOption);
        command.Options.Add(hostIpOption);
        command.Options.Add(fhirVersionOption);
        command.Options.Add(authHeaderOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var tests = parseResult.GetValue(testsOption)!;
            var port = parseResult.GetValue(portOption);
            var hostIp = parseResult.GetValue(hostIpOption)!;
            var fhirVersion = parseResult.GetValue(fhirVersionOption);
            var authHeader = parseResult.GetValue(authHeaderOption);
            return RunAsync(tests, port, hostIp, fhirVersion, authHeader, cancellationToken);
        });

        return command;
    }

    /// <summary>
    /// Loads the TestScript registry, wires up the FHIR target cache (client-credentials token auth
    /// when FHIR_TOKEN_URL is configured, otherwise a static auth header), and runs the Kestrel host
    /// until the command's <paramref name="cancellationToken"/> fires (Ctrl+C), then shuts down and
    /// returns 0.
    /// </summary>
    internal static async Task<int> RunAsync(
        string testsPath, int port, string hostIp, string? fhirVersion, string? authHeader, CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(testsPath))
            {
                Console.Error.WriteLine($"error: --tests directory not found: {testsPath}");
                return RunCommand.UsageErrorExitCode;
            }

            if (!Directory.EnumerateFiles(testsPath, "*.json", SearchOption.AllDirectories).Any())
            {
                Console.Error.WriteLine($"error: no .json files found in {testsPath} — nothing to serve");
                return RunCommand.UsageErrorExitCode;
            }

            if (port is < 1 or > 65535)
            {
                Console.Error.WriteLine($"error: --port must be between 1 and 65535, got {port}");
                return RunCommand.UsageErrorExitCode;
            }

            var tokenConfig = TokenClientConfiguration.FromEnvironment();

            // FHIR_TOKEN_URL takes precedence over a static header, whether it came from --auth-header
            // or its FHIR_AUTH_HEADER environment equivalent (used when the flag is omitted).
            var effectiveAuthHeader = tokenConfig is not null
                ? null
                : authHeader ?? Environment.GetEnvironmentVariable("FHIR_AUTH_HEADER");

            if (effectiveAuthHeader is not null)
            {
                // Fail fast on a bad --auth-header/FHIR_AUTH_HEADER, the same way RunCommand does,
                // instead of only discovering it on the first /run call.
                using var probe = new HttpClient();
                if (RunCommand.ApplyAuthHeader(probe, effectiveAuthHeader) is { } authError)
                {
                    Console.Error.WriteLine($"error: {authError}");
                    return RunCommand.UsageErrorExitCode;
                }
            }

            var registry = TestScriptRegistry.Load(testsPath);
            var schema = new R4CoreSchemaProvider();

            Func<HttpMessageHandler>? handlerFactory = tokenConfig is not null
                ? () => new ClientCredentialsTokenHandler(tokenConfig) { InnerHandler = new HttpClientHandler() }
                : null;

            using var targetCache = new FhirTargetCache(effectiveAuthHeader, handlerFactory);

            var app = RunnerHost.Create(registry, targetCache, schema, hostIp, port, fhirVersion);

            Console.WriteLine(
                $"ignixa-matrix serve: {registry.Count} script(s) loaded ({registry.InvalidCount} invalid), " +
                $"token auth: {(tokenConfig is not null ? "enabled" : "disabled")}");

            await app.StartAsync(cancellationToken);
            Console.WriteLine($"listening on {string.Join(", ", app.Urls)}");

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ctrl+C — fall through to graceful shutdown below.
            }

            await app.StopAsync(CancellationToken.None);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: unexpected failure: {ex}");
            return RunCommand.InternalErrorExitCode;
        }
    }
}

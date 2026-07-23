// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Globalization;
using Ignixa.Serialization;
using Ignixa.Specification.Extensions;
using Ignixa.TestScript.Locust.Artifacts;
using Ignixa.TestScript.Locust.Compilation;
using Ignixa.TestScript.Locust.Diagnostics;
using Ignixa.TestScript.Locust.Ir;
using Ignixa.TestScript.Parsing;

namespace Ignixa.ConformanceMatrix.Cli.Commands;

/// <summary>
/// Compiles a single TestScript definition into a flat Locust load-test artifact: parses the
/// TestScript, lowers it against the selected FHIR schema, prints diagnostics, and atomically
/// writes the resulting five-file artifact directory.
/// </summary>
internal static class CompileLocustCommand
{
    internal const int SuccessExitCode = 0;
    internal const int CompilationErrorExitCode = 1;
    internal const int UsageErrorExitCode = 2;
    internal const int UnexpectedErrorExitCode = 3;

    private const string ParseDiagnosticCode = "TESTSCRIPT_PARSE";

    public static Command Build()
    {
        var command = new Command("compile-locust", "Compile a TestScript definition into a flat Locust load-test artifact");

        // Arity = ZeroOrOne on every option (rather than the default ExactlyOne) so a dangling flag
        // (present with no following value) still lets this action run and return our controlled
        // usage exit code, instead of a System.CommandLine parser-level arity error short-circuiting
        // the invocation with its own default exit code.
        var testOption = new Option<string?>("--test")
        {
            Description = "Path to the TestScript JSON definition to compile",
            Arity = ArgumentArity.ZeroOrOne
        };
        var outOption = new Option<string?>("--out")
        {
            Description = "Output directory for the compiled Locust artifact (created or replaced atomically)",
            Arity = ArgumentArity.ZeroOrOne
        };
        var fhirVersionOption = new Option<string?>("--fhir-version")
        {
            Description = "Target FHIR version: 4.0, 4.3, or 5.0",
            Arity = ArgumentArity.ZeroOrOne
        };
        var fixtureVariantsOption = new Option<string?>("--fixture-variants")
        {
            Description = "Number of fixture resource variants to generate (positive integer; required for fhirfakes fixtures)",
            Arity = ArgumentArity.ZeroOrOne
        };

        command.Options.Add(testOption);
        command.Options.Add(outOption);
        command.Options.Add(fhirVersionOption);
        command.Options.Add(fixtureVariantsOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var testPath = parseResult.GetValue(testOption);
            var outPath = parseResult.GetValue(outOption);
            var fhirVersion = parseResult.GetValue(fhirVersionOption);

            // GetResult (rather than GetValue) is the only way to tell "the flag was present with no
            // value" apart from "the flag was never supplied" -- both yield a null GetValue here, but
            // only the former must be rejected instead of silently forwarded as the omitted default.
            var fixtureVariantsSupplied = parseResult.GetResult(fixtureVariantsOption) is not null;
            var fixtureVariantsRaw = parseResult.GetValue(fixtureVariantsOption);

            // Route through the invocation's own writers (which default to Console.Out/Console.Error
            // but can be redirected by callers, including tests, via InvocationConfiguration) rather
            // than the global Console directly.
            var standardOutput = parseResult.InvocationConfiguration.Output;
            var standardError = parseResult.InvocationConfiguration.Error;

            if (fixtureVariantsSupplied && string.IsNullOrWhiteSpace(fixtureVariantsRaw))
            {
                standardError.WriteLine("error: --fixture-variants requires a positive integer value");
                return Task.FromResult(UsageErrorExitCode);
            }

            // Parsed here as a raw string (rather than as a typed System.CommandLine Option<int>)
            // so a malformed value reliably returns our controlled usage exit code instead of the
            // parser's own default exit code for an option-level validation failure.
            if (!TryParseFixtureVariants(fixtureVariantsRaw, standardError, out var fixtureVariants))
            {
                return Task.FromResult(UsageErrorExitCode);
            }

            return RunAsync(testPath, outPath, fhirVersion, fixtureVariants, standardOutput, standardError, cancellationToken);
        });

        return command;
    }

    /// <summary>
    /// Runs the compile-locust command, writing diagnostics and the compiled artifact through
    /// <see cref="Console.Out"/> and <see cref="Console.Error"/>.
    /// </summary>
    internal static Task<int> RunAsync(
        string? testPath,
        string? outPath,
        string? fhirVersion,
        int? fixtureVariants,
        CancellationToken cancellationToken)
        => RunAsync(testPath, outPath, fhirVersion, fixtureVariants, Console.Out, Console.Error, cancellationToken);

    /// <summary>
    /// Runs the compile-locust command, writing diagnostics and the compiled artifact through the
    /// given writers. Used directly by tests to avoid races on the shared <see cref="Console"/>.
    /// </summary>
    internal static Task<int> RunAsync(
        string? testPath,
        string? outPath,
        string? fhirVersion,
        int? fixtureVariants,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken)
        => RunAsync(testPath, outPath, fhirVersion, fixtureVariants, standardOutput, standardError, WriteArtifactAsync, cancellationToken);

    /// <summary>
    /// Runs the compile-locust command with an injectable artifact-writing delegate. Production
    /// callers always pass <see cref="WriteArtifactAsync"/>; tests use this overload directly to
    /// deterministically simulate an artifact-writing failure without faking the parser or compiler.
    /// </summary>
    internal static async Task<int> RunAsync(
        string? testPath,
        string? outPath,
        string? fhirVersion,
        int? fixtureVariants,
        TextWriter standardOutput,
        TextWriter standardError,
        Func<LocustIrDocument, IReadOnlyList<LocustDiagnostic>, string, CancellationToken, Task> writeArtifactAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(writeArtifactAsync);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(testPath))
            {
                standardError.WriteLine("error: --test is required");
                return UsageErrorExitCode;
            }

            if (string.IsNullOrWhiteSpace(outPath))
            {
                standardError.WriteLine("error: --out is required");
                return UsageErrorExitCode;
            }

            if (string.IsNullOrWhiteSpace(fhirVersion))
            {
                standardError.WriteLine("error: --fhir-version is required");
                return UsageErrorExitCode;
            }

            if (fhirVersion is not ("4.0" or "4.3" or "5.0"))
            {
                standardError.WriteLine($"error: unsupported --fhir-version '{fhirVersion}'; expected 4.0, 4.3, or 5.0");
                return UsageErrorExitCode;
            }

            if (fixtureVariants is <= 0)
            {
                standardError.WriteLine(
                    $"error: --fixture-variants must be a positive integer; got '{fixtureVariants}'");
                return UsageErrorExitCode;
            }

            if (!TryGetFullPath(testPath, out var fullTestPath))
            {
                standardError.WriteLine($"error: --test path is not valid: '{testPath}'");
                return UsageErrorExitCode;
            }

            if (!File.Exists(fullTestPath))
            {
                standardError.WriteLine($"error: --test file not found: '{testPath}'");
                return UsageErrorExitCode;
            }

            if (!TryGetFullPath(outPath, out var fullOutPath))
            {
                standardError.WriteLine($"error: --out path is not valid: '{outPath}'");
                return UsageErrorExitCode;
            }

            if (File.Exists(fullOutPath))
            {
                standardError.WriteLine($"error: --out path already exists as a file: '{outPath}'");
                return UsageErrorExitCode;
            }

            var version = FhirSpecificationExtensions.FromVersionString(fhirVersion);
            var schema = version.GetSchemaProvider();

            cancellationToken.ThrowIfCancellationRequested();

            var parseResult = TestScriptParser.ParseFile(testPath);
            List<LocustDiagnostic> parserDiagnostics = [.. parseResult.Errors.Select(error => new LocustDiagnostic(
                ParseDiagnosticCode,
                error.Severity == ParseSeverity.Warning
                    ? LocustDiagnosticSeverity.Warning
                    : LocustDiagnosticSeverity.Error,
                $"{testPath}:{error.Path ?? "$"}",
                error.Message))];

            PrintDiagnostics(parserDiagnostics, standardError);

            if (!parseResult.IsSuccess || parseResult.Value is null)
            {
                return CompilationErrorExitCode;
            }

            var options = new LocustCompilerOptions(
                Path.GetFileName(testPath),
                fhirVersion,
                schema,
                fixtureVariants ?? 0);

            var compilation = await new LocustIrCompiler()
                .CompileAsync(parseResult.Value, options, cancellationToken)
                .ConfigureAwait(false);

            PrintDiagnostics(compilation.Diagnostics, standardError);

            if (compilation.HasErrors || compilation.Document is null)
            {
                return CompilationErrorExitCode;
            }

            List<LocustDiagnostic> allDiagnostics = [.. parserDiagnostics, .. compilation.Diagnostics];

            await writeArtifactAsync(compilation.Document, allDiagnostics, outPath, cancellationToken)
                .ConfigureAwait(false);

            standardOutput.WriteLine($"Compiled Locust artifact -> {outPath}");
            return SuccessExitCode;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            standardError.WriteLine($"error: {ex.GetType().Name}: {ex.Message}");
            return UnexpectedErrorExitCode;
        }
    }

    private static bool TryParseFixtureVariants(string? raw, TextWriter standardError, out int? value)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = null;
            return true;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            standardError.WriteLine($"error: --fixture-variants must be a positive integer; got '{raw}'");
            value = null;
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>
    /// Production implementation of the injectable artifact-writer delegate used by
    /// <see cref="RunAsync(string?, string?, string?, int?, TextWriter, TextWriter, CancellationToken)"/>.
    /// Kept as a separate method (rather than inlined) purely so tests can substitute a deterministic
    /// failing delegate at the internal seam without faking the parser or compiler.
    /// </summary>
    private static Task WriteArtifactAsync(
        LocustIrDocument document,
        IReadOnlyList<LocustDiagnostic> diagnostics,
        string outputDirectory,
        CancellationToken cancellationToken)
        => new LocustArtifactWriter().WriteAsync(document, diagnostics, outputDirectory, cancellationToken);

    private static bool TryGetFullPath(string path, out string fullPath)
    {
        try
        {
            fullPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            _ = ex;
            fullPath = string.Empty;
            return false;
        }
    }

    private static void PrintDiagnostics(IEnumerable<LocustDiagnostic> diagnostics, TextWriter writer)
    {
        foreach (var diagnostic in diagnostics)
        {
            // Informational metric-mapping diagnostics are persisted to diagnostics.json but never
            // printed; only warnings and errors are surfaced to the console.
            if (diagnostic.Severity == LocustDiagnosticSeverity.Info)
            {
                continue;
            }

            var severityText = diagnostic.Severity == LocustDiagnosticSeverity.Warning ? "warning" : "error";
            writer.WriteLine($"{severityText} {diagnostic.Code} {diagnostic.Source}: {diagnostic.Message}");
        }
    }
}

using System.CommandLine;
using Ignixa.Application.Infrastructure;
using Ignixa.DataLayer.SqlServer;
using Ignixa.Domain.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SqlServer.Dac;

namespace Ignixa.SchemaUpgrade.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
        => await CreateRootCommand().Parse(args).InvokeAsync();

    internal static RootCommand CreateRootCommand()
    {
        var tenantIdOption = new Option<int>("--tenant-id") { Required = true, Description = "The tenant ID to upgrade." };
        var confirmOption = new Option<bool>("--confirm") { Description = "Apply the upgrade without an interactive prompt (for scripted/CI use)." };
        var allowDataLossOption = new Option<bool>("--allow-data-loss") { Description = "Permit the deploy to proceed even when SqlPackage/DacFx would otherwise block it as possibly data-lossy. Required to apply diffs flagged unsafe by DeployReportClassifier." };
        var allowIncompatiblePlatformOption = new Option<bool>("--allow-incompatible-platform") { Description = "Permit the deploy to proceed when the target server's platform differs from the dacpac's target platform. The schema targets Azure SQL Database, so this is required when deploying to a box SQL Server (local development, on-premises, or a test container)." };
        var configOption = new Option<string>("--config") { Description = "Path to a JSON configuration file with tenant connection settings. Defaults to appsettings.json in the current working directory.", DefaultValueFactory = _ => "appsettings.json" };

        var rootCommand = new RootCommand("Reviews and applies a pending schema upgrade for a tenant database that SchemaDeployer's automatic path refused.");
        rootCommand.Options.Add(tenantIdOption);
        rootCommand.Options.Add(confirmOption);
        rootCommand.Options.Add(allowDataLossOption);
        rootCommand.Options.Add(allowIncompatiblePlatformOption);
        rootCommand.Options.Add(configOption);

        rootCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var options = new CliUpgradeOptions(
                TenantId: parseResult.GetValue(tenantIdOption),
                AutoConfirm: parseResult.GetValue(confirmOption),
                AllowDataLoss: parseResult.GetValue(allowDataLossOption),
                AllowIncompatiblePlatform: parseResult.GetValue(allowIncompatiblePlatformOption),
                ConfigPath: parseResult.GetValue(configOption) ?? "appsettings.json");

            try
            {
                return await RunAsync(options, Console.In, Console.Out, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Exit code 3 is deliberately distinct from exit code 1 ("the operator declined
                // the confirmation prompt; nothing was applied"): without this boundary catch,
                // System.CommandLine's default exception handler also returns 1 for any unhandled
                // exception, so a scripted caller branching on --confirm's exit code could not
                // tell "declined" from "crashed before doing anything" apart. This is exactly the
                // failure mode for an unknown/inactive tenant, a non-SQL-Server or misconfigured
                // storage type, an unparseable connection string, a missing --config file, or a
                // missing embedded schema dacpac -- see TenantConnectionStringResolver.ResolveForSchemaDeploymentAsync
                // and RunAsync's AddJsonFile/GetManifestResourceStream calls.
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 3;
            }
        });

        return rootCommand;
    }

    internal static async Task<int> RunAsync(
        CliUpgradeOptions options,
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var (tenantId, autoConfirm, allowDataLoss, allowIncompatiblePlatform, configPath) = options;

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(configPath, optional: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        ITenantConfigurationStore tenantConfigurationStore =
            new AppSettingsTenantConfigurationStore(configuration, NullLogger<AppSettingsTenantConfigurationStore>.Instance);

        var connectionString = await TenantConnectionStringResolver.ResolveForSchemaDeploymentAsync(tenantConfigurationStore, tenantId, cancellationToken);

        using var dacpacStream = typeof(SchemaDeployer).Assembly.GetManifestResourceStream("Ignixa.DataLayer.SqlServer.Schema.dacpac")
            ?? throw new InvalidOperationException("Embedded schema dacpac not found.");
        using var package = DacPackage.Load(dacpacStream);
        var databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        var dacServices = new DacServices(connectionString);

        // One options object for both the report and the deploy: a report generated under
        // different options describes a different operation than the one that will actually run.
        var deployOptions = new DacDeployOptions
        {
            BlockOnPossibleDataLoss = !allowDataLoss,
            AllowIncompatiblePlatform = allowIncompatiblePlatform,
        };

        var deployReportXml = dacServices.GenerateDeployReport(
            package, databaseName, options: deployOptions, cancellationToken: cancellationToken);
        output.WriteLine($"Pending schema diff for tenant {tenantId} ({databaseName}):");
        output.WriteLine(deployReportXml);
        output.WriteLine();

        var classification = DeployReportClassifier.Classify(deployReportXml);
        output.WriteLine(classification.Outcome switch
        {
            DeployClassification.AutoSafe =>
                "This diff IS classified as auto-safe -- SchemaDeployer's automatic path should have applied it. Applying it here anyway is redundant but harmless.",
            DeployClassification.Unsafe =>
                $"This diff is NOT auto-safe -- DacFx flagged: {classification.ReasonSummary}. Review the XML above carefully before proceeding. " +
                "If you proceed and DacFx still blocks the deploy citing possible data loss, re-run with --allow-data-loss.",
            // Unclassifiable is deliberately a prompt, not a crash: this tool exists precisely to
            // let an operator apply what the automatic path refused, so a report the classifier
            // can't read must still reach a human decision rather than terminating here.
            _ =>
                $"This diff could NOT be classified: {classification.ReasonSummary}. The automatic path will refuse it. " +
                "Review the XML above especially carefully -- the usual data-loss signal could not be verified.",
        });

        if (!ConfirmApply(autoConfirm, input, output))
        {
            output.WriteLine("Aborted, nothing was applied.");
            return 1;
        }

        // Deploy and stamp go through the same paired SchemaDeployer method its own automatic
        // paths use, so this deploy can never be left unstamped by a call this method forgot to
        // make. A stamp failure must NOT be reported the same way as "aborted, nothing applied"
        // (exit 1) -- an operator who sees that after a destructive --allow-data-loss run would
        // reasonably conclude their change didn't land and re-run it. Report the partial state
        // explicitly with its own exit code.
        var result = await SchemaDeployer.DeployAndStampAsync(
            dacServices, package, databaseName, connectionString, deployOptions,
            SchemaVersionConstants.CurrentVersion, cancellationToken);

        if (result.Outcome == SchemaDeployOutcome.AppliedButVersionStampFailed)
        {
            output.WriteLine(
                $"WARNING: the schema WAS applied to tenant {tenantId}'s database, but recording schema version " +
                $"{SchemaVersionConstants.CurrentVersion} in dbo.SchemaVersion failed: {result.StampException!.Message}");
            output.WriteLine(
                "The database is up to date; only the version record is missing. Do NOT re-run this tool expecting " +
                "the schema change to be re-applied -- instead re-run it once the underlying error is resolved, or " +
                "insert the version row manually.");
            return 2;
        }

        output.WriteLine($"Applied. Tenant {tenantId}'s database is now on the current schema.");
        return 0;
    }

    /// <summary>
    /// Split out from <see cref="RunAsync"/> so the decline path -- exit code 1, nothing applied --
    /// is unit-testable without a live tenant database (RunAsync's report-generation/deploy calls need one).
    /// </summary>
    internal static bool ConfirmApply(bool autoConfirm, TextReader input, TextWriter output)
    {
        if (autoConfirm)
        {
            return true;
        }

        output.Write("Apply this diff? [y/N] ");
        var response = input.ReadLine();
        return string.Equals(response, "y", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Bundles <see cref="Program.RunAsync"/>'s two same-typed boolean flags into named fields so a
/// positional argument transposition (swapping "skip the confirmation prompt" with "permit data
/// loss") is a compile error instead of a silent, behavior-changing bug.
/// </summary>
internal sealed record CliUpgradeOptions(int TenantId, bool AutoConfirm, bool AllowDataLoss, bool AllowIncompatiblePlatform, string ConfigPath);

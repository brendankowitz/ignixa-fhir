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
        var configOption = new Option<string>("--config") { Description = "Path to a JSON configuration file with tenant connection settings. Defaults to appsettings.json in the current working directory.", DefaultValueFactory = _ => "appsettings.json" };

        var rootCommand = new RootCommand("Reviews and applies a pending schema upgrade for a tenant database that SchemaDeployer's automatic path refused.");
        rootCommand.Options.Add(tenantIdOption);
        rootCommand.Options.Add(confirmOption);
        rootCommand.Options.Add(allowDataLossOption);
        rootCommand.Options.Add(configOption);

        rootCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var tenantId = parseResult.GetValue(tenantIdOption);
            var autoConfirm = parseResult.GetValue(confirmOption);
            var allowDataLoss = parseResult.GetValue(allowDataLossOption);
            var configPath = parseResult.GetValue(configOption) ?? "appsettings.json";

            return await RunAsync(tenantId, autoConfirm, allowDataLoss, configPath, Console.In, Console.Out, cancellationToken);
        });

        return rootCommand;
    }

    internal static async Task<int> RunAsync(
        int tenantId,
        bool autoConfirm,
        bool allowDataLoss,
        string configPath,
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(configPath, optional: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        ITenantConfigurationStore tenantConfigurationStore =
            new AppSettingsTenantConfigurationStore(configuration, NullLogger<AppSettingsTenantConfigurationStore>.Instance);

        var connectionString = await TenantConnectionStringResolver.ResolveAsync(tenantConfigurationStore, tenantId, cancellationToken);

        using var dacpacStream = typeof(SchemaDeployer).Assembly.GetManifestResourceStream("Ignixa.DataLayer.SqlServer.Schema.dacpac")
            ?? throw new InvalidOperationException("Embedded schema dacpac not found.");
        using var package = DacPackage.Load(dacpacStream);
        var databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        var dacServices = new DacServices(connectionString);

        var deployReportXml = dacServices.GenerateDeployReport(package, databaseName, cancellationToken: cancellationToken);
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

        var deployOptions = new DacDeployOptions { BlockOnPossibleDataLoss = !allowDataLoss };
        dacServices.Deploy(package, databaseName, upgradeExisting: true, options: deployOptions, cancellationToken: cancellationToken);

        // The schema is applied from here on. A failure stamping dbo.SchemaVersion must NOT be
        // reported the same way as "aborted, nothing applied" (exit 1) -- an operator who sees that
        // after a destructive --allow-data-loss run would reasonably conclude their change didn't
        // land and re-run it. Report the partial state explicitly with its own exit code.
        try
        {
            await SchemaDeployer.StampSchemaVersionAsync(connectionString, SchemaVersionConstants.CurrentVersion, cancellationToken);
        }
        catch (Exception ex)
        {
            output.WriteLine(
                $"WARNING: the schema WAS applied to tenant {tenantId}'s database, but recording schema version " +
                $"{SchemaVersionConstants.CurrentVersion} in dbo.SchemaVersion failed: {ex.Message}");
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

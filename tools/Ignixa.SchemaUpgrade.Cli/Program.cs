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

        var rootCommand = new RootCommand("Reviews and applies a pending schema upgrade for a tenant database that SchemaDeployer's automatic path refused.");
        rootCommand.Options.Add(tenantIdOption);
        rootCommand.Options.Add(confirmOption);

        rootCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var tenantId = parseResult.GetValue(tenantIdOption);
            var autoConfirm = parseResult.GetValue(confirmOption);

            return await RunAsync(tenantId, autoConfirm, Console.In, Console.Out, cancellationToken);
        });

        return rootCommand;
    }

    internal static async Task<int> RunAsync(
        int tenantId,
        bool autoConfirm,
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        ITenantConfigurationStore tenantConfigurationStore =
            new AppSettingsTenantConfigurationStore(configuration, NullLogger<AppSettingsTenantConfigurationStore>.Instance);

        var connectionString = await SqlExecutionService.ResolveConnectionStringAsync(tenantConfigurationStore, tenantId, cancellationToken);

        using var dacpacStream = typeof(SchemaDeployer).Assembly.GetManifestResourceStream("Ignixa.DataLayer.SqlServer.Schema.dacpac")
            ?? throw new InvalidOperationException("Embedded schema dacpac not found.");
        using var package = DacPackage.Load(dacpacStream);
        var databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        var dacServices = new DacServices(connectionString);

        var deployReportXml = dacServices.GenerateDeployReport(package, databaseName, cancellationToken: cancellationToken);
        output.WriteLine($"Pending schema diff for tenant {tenantId} ({databaseName}):");
        output.WriteLine(deployReportXml);
        output.WriteLine();
        output.WriteLine(DeployReportClassifier.IsAutoSafe(deployReportXml)
            ? "This diff IS classified as auto-safe -- SchemaDeployer's automatic path should have applied it. Applying it here anyway is redundant but harmless."
            : "This diff is NOT classified as auto-safe -- it contains operations outside the known-benign allow-list. Review the XML above carefully before proceeding.");

        if (!ConfirmApply(autoConfirm, input, output))
        {
            output.WriteLine("Aborted, nothing was applied.");
            return 1;
        }

        dacServices.Deploy(package, databaseName, upgradeExisting: true, cancellationToken: cancellationToken);
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

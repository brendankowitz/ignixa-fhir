namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Configuration for the SQL Server data layer's schema deployment, bound from the
/// <see cref="SectionName"/> configuration section.
/// </summary>
public sealed class SqlServerOptions
{
    public const string SectionName = "SqlServer";

    /// <summary>
    /// Whether the app may apply schema changes itself. Defaults to <c>false</c>, so a deployment
    /// opts in rather than out. When <c>true</c>, brand-new tenant databases are provisioned from
    /// the embedded dacpac and tenants behind the current version are upgraded -- but only when the
    /// pending diff classifies as auto-safe; <see cref="DeployReportClassifier"/> still gates every
    /// upgrade, and this flag never bypasses it. When <c>false</c>, both cases throw and direct the
    /// operator to the schema-upgrade CLI instead.
    /// </summary>
    public bool AutomaticSchemaDeploymentEnabled { get; set; }

    /// <summary>
    /// Lets DacFx deploy this project's dacpac to a server whose platform does not match the
    /// dacpac's target platform (see <c>DacDeployOptions.AllowIncompatiblePlatform</c>).
    /// <para>
    /// Test-only, and deliberately <c>internal</c>. The schema targets Azure SQL Database because
    /// that is the production deployment target, which makes any box SQL Server the incompatible
    /// side of the pairing. Non-Production hosts already get this automatically (see
    /// <c>SchemaDeployer.CreateDeployOptions</c>), so this exists only for tests that deliberately
    /// construct the deployer with a <c>Production</c> environment in order to exercise the strict
    /// path, while still running against a box SQL Server.
    /// </para>
    /// <para>
    /// Keeping it internal rather than public means <see cref="Microsoft.Extensions.Configuration"/>'s
    /// binder -- which only binds public properties -- cannot set it, so no appsettings entry or
    /// environment variable can turn cross-platform deployment on in a deployed app.
    /// </para>
    /// </summary>
    internal bool AllowIncompatiblePlatform { get; set; }
}

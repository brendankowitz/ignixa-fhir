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

    /// <summary>
    /// The default for <see cref="TerminologyImportCommandTimeoutSeconds"/>, also used by call sites (tests,
    /// mainly) that construct the importer without reading configuration.
    /// </summary>
    public const int DefaultTerminologyImportCommandTimeoutSeconds = 120;

    /// <summary>
    /// <see cref="Microsoft.Data.SqlClient.SqlCommand.CommandTimeout"/> for <c>dbo.ImportTermCodeSystem</c>,
    /// <c>dbo.ImportTermValueSet</c> and <c>dbo.ImportTermConceptMap</c> -- the three commands that carry a
    /// whole CodeSystem, ValueSet or ConceptMap as a table-valued parameter and run its insert, delete-and-
    /// replace, and hierarchy resolution in one server-side transaction -- and also for the reads
    /// <c>SqlServerValueSetComposer</c> runs to resolve a ValueSet's <c>compose</c> element before
    /// <c>dbo.ImportTermValueSet</c> ever runs. Those reads can be just as large: an include naming a whole
    /// CodeSystem with no <c>concept</c> or <c>filter</c> array reads every concept in it, and an include
    /// naming a previously expanded ValueSet reads every one of its rows. Left unset, <see cref="SqlCommand"/>
    /// defaults to 30 seconds, and SQL error <c>-2</c> (command timeout) is classified transient by
    /// <c>SqlExecutionService.IsTransient</c>, so a command that overruns the timeout is retried up to three
    /// more times before the import is marked <c>Failed</c> -- and <c>Failed</c> is not a terminal status, so
    /// the whole package is re-offered and re-fails on every subsequent startup.
    /// <para>
    /// Measured against a local, otherwise-idle SQL Server: importing 100,000 flat concepts (no properties or
    /// definitions) took under 2 seconds, a 350,000-concept import (SNOMED CT's rough scale) took under 6
    /// seconds, and re-importing 100,000 concepts -- the cascade DELETE of the previous import plus a full
    /// re-insert -- took about 3 seconds. Real CodeSystems carry per-concept <c>property</c> and
    /// <c>designation</c> payloads this benchmark did not, and a production database adds network latency, a
    /// lower-throughput SKU, and lock contention from concurrent terminology activity to that baseline. The
    /// default below is set well above the measured numbers rather than at them, without being an unbounded
    /// escape hatch: a genuinely stuck command still fails within two minutes instead of hanging forever.
    /// </para>
    /// </summary>
    public int TerminologyImportCommandTimeoutSeconds { get; set; } = DefaultTerminologyImportCommandTimeoutSeconds;
}

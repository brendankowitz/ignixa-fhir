namespace Ignixa.DataLayer.SqlServer;

public sealed class SqlServerOptions
{
    public const string SectionName = "SqlServer";

    public bool AutomaticSchemaDeploymentEnabled { get; set; }
}

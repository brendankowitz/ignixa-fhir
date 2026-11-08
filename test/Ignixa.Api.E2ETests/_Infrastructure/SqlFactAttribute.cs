namespace Ignixa.Api.E2ETests._Infrastructure;

/// <summary>A fact that needs a real SQL Server; skipped when the suite runs on file storage.</summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (SqlTestEnvironment.UsesFileSystem)
        {
            Skip = SqlTestEnvironment.SkipReason;
        }
    }
}

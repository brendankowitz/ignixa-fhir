namespace Ignixa.Api.E2ETests._Infrastructure;

/// <summary>A theory that needs a real SQL Server; skipped when the suite runs on file storage.</summary>
public sealed class SqlTheoryAttribute : TheoryAttribute
{
    public SqlTheoryAttribute()
    {
        if (SqlTestEnvironment.UsesFileSystem)
        {
            Skip = SqlTestEnvironment.SkipReason;
        }
    }
}

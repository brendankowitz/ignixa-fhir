namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Marks this assembly as existing and buildable before any real functionality lands (Task 1 of
/// Phase A). Removed once Task 2 adds ISqlExecutionService -- a project with zero types is not a
/// meaningful "it builds" proof; this gives the build something concrete to compile and this task's
/// test something concrete to assert against.
/// </summary>
public static class AssemblyMarker
{
    public const string ProjectName = "Ignixa.DataLayer.SqlServer";
}

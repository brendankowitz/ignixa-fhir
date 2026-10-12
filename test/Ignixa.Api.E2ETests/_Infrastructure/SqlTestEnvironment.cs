namespace Ignixa.Api.E2ETests._Infrastructure;

internal static class SqlTestEnvironment
{
    public const string SkipReason = "Requires SQL Server; TEST_USE_FILESYSTEM is set.";

    public static bool UsesFileSystem =>
        Environment.GetEnvironmentVariable("TEST_USE_FILESYSTEM")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
}

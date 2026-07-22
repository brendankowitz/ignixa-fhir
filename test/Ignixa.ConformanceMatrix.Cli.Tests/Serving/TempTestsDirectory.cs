namespace Ignixa.ConformanceMatrix.Cli.Tests.Serving;

/// <summary>A throwaway folder of TestScript .json files for registry/host tests, cleaned up on Dispose.</summary>
internal sealed class TempTestsDirectory : IDisposable
{
    public TempTestsDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), $"ignixa-matrix-serving-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public void WriteScript(string relativePath, string content)
    {
        var fullPath = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(fullPath, content);
    }

    public static string ValidScriptJson(string name) => $$"""
        {
          "resourceType": "TestScript",
          "name": "{{name}}",
          "status": "active",
          "test": [
            {
              "name": "noop",
              "action": [
                { "operation": { "type": { "code": "read" }, "url": "health/check" } }
              ]
            }
          ]
        }
        """;

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory must not fail an otherwise passing test.
        }
    }
}

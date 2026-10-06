namespace Ignixa.PackageManagement.Tests.Acquisition;

internal sealed class OwnedCacheDirectory : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(
        Directory.GetCurrentDirectory(), "artifacts", "a3-cache", Guid.NewGuid().ToString("N"));

    internal OwnedCacheDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(Path))
        {
            File.Delete(file);
        }
        Directory.Delete(Path);
    }
}

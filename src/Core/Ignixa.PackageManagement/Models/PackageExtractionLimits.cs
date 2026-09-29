namespace Ignixa.PackageManagement.Models;

/// <summary>Independent inclusive limits for opt-in, in-memory strict extraction.</summary>
/// <remarks>
/// Byte limits are positive <see cref="int"/> values because extraction buffers in memory.
/// Callers adapting long-valued policies must reject values above <see cref="int.MaxValue"/>
/// and use checked conversions rather than truncating.
/// </remarks>
public sealed class PackageExtractionLimits
{
    public PackageExtractionLimits(
        int maxCompressedBytes = 32 * 1024 * 1024,
        int maxExpandedBytes = 256 * 1024 * 1024,
        int maxEntryBytes = 16 * 1024 * 1024,
        int maxArchiveEntries = 10_000,
        int maxJsonDepth = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCompressedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExpandedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntryBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxArchiveEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxJsonDepth);
        MaxCompressedBytes = maxCompressedBytes;
        MaxExpandedBytes = maxExpandedBytes;
        MaxEntryBytes = maxEntryBytes;
        MaxArchiveEntries = maxArchiveEntries;
        MaxJsonDepth = maxJsonDepth;
    }

    public int MaxCompressedBytes { get; }
    public int MaxExpandedBytes { get; }
    public int MaxEntryBytes { get; }
    public int MaxArchiveEntries { get; }
    public int MaxJsonDepth { get; }
}

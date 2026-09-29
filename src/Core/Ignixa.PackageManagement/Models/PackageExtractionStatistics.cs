namespace Ignixa.PackageManagement.Models;

/// <summary>
/// ExpandedBytes includes headers, metadata, padding and end blocks. PhysicalEntries includes
/// Pax/GNU metadata headers; LogicalEntries includes directories and ignored non-JSON files.
/// PayloadBytes includes every physical entry's payload, including ignored and metadata entries.
/// </summary>
/// <param name="CompressedBytes">Actual input bytes from the caller's current position through EOF.</param>
/// <param name="ExpandedBytes">All decompressed tar bytes, including complete zero record padding.</param>
/// <param name="PayloadBytes">Sum of physical bodies, excluding headers and block padding.</param>
/// <param name="PhysicalEntries">Nonzero headers, including local Pax/GNU metadata.</param>
/// <param name="LogicalEntries">Files/directories reconciled with physical frames, including manifest and ignored files.</param>
public sealed record PackageExtractionStatistics(
    long CompressedBytes,
    long ExpandedBytes,
    long PayloadBytes,
    int PhysicalEntries,
    int LogicalEntries);

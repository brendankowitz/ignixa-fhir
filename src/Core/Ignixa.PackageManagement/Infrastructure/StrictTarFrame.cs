namespace Ignixa.PackageManagement.Infrastructure;

internal readonly record struct StrictTarFrame(
    int PhysicalIndex,
    int DataOffset,
    long Size,
    bool Directory,
    string Path);

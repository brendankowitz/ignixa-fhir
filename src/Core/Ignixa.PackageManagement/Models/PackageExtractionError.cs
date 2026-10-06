namespace Ignixa.PackageManagement.Models;

public enum PackageExtractionError
{
    CompressedSizeLimit,
    ExpandedSizeLimit,
    EntrySizeLimit,
    EntryCountLimit,
    JsonDepthLimit,
    InvalidArchive,
    UnsafePath,
    DuplicatePath,
    UnsupportedEntryType,
    MissingManifest,
    DuplicateManifest,
    InvalidManifest,
    InvalidJson
}

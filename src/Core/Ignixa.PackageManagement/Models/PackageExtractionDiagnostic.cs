namespace Ignixa.PackageManagement.Models;

/// <summary>
/// Content-free extraction diagnostic. Coordinates always count physical tar headers,
/// including hidden Pax/GNU metadata, directories and ignored files.
/// </summary>
/// <param name="Code">Validation category, without untrusted parser text.</param>
/// <param name="EntryIndex">
/// One-based physical header index, or null for archive-wide failures. Resolved-path/JSON validation
/// failures identify the file/directory's data header. Metadata decoding failures identify the metadata
/// header carrying the malformed bytes, including invalid UTF-8 in a GNU long path. Reader disagreement
/// identifies the expected physical entry; missing/trailing framing may identify the next expected index.
/// </param>
/// <param name="Limit">Configured inclusive maximum for a numeric limit failure; otherwise null.</param>
public sealed record PackageExtractionDiagnostic(
    PackageExtractionError Code,
    int? EntryIndex = null,
    long? Limit = null);

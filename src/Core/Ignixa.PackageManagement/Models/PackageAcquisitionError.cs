namespace Ignixa.PackageManagement.Models;

public enum PackageAcquisitionError
{
    InvalidMetadata,
    MetadataIdentityMismatch,
    UntrustedUri,
    RedirectRejected,
    MissingIntegrity,
    InvalidIntegrity,
    IntegrityConflict,
    DigestMismatch,
    ManifestIdentityMismatch,
    MetadataSizeLimit,
    CompressedSizeLimit,
    UnsupportedContentEncoding,
    HttpFailure,
    TransportFailure,
    Timeout,
    AuthenticationFailure,
    CacheFailure
}

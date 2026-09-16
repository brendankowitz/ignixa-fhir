---
sidebar_position: 9
title: Package Management
description: FHIR package management and loading
---

# Ignixa.PackageManagement

Download, cache, and load FHIR implementation guide packages from NPM registries.

## Installation

```bash
dotnet add package Ignixa.PackageManagement
```

## Quick Start

```csharp
using Ignixa.PackageManagement;
using Ignixa.PackageManagement.Infrastructure;
using Microsoft.Extensions.Logging;

// Create package loader with caching
var cacheManager = new PackageCacheManager("/var/cache/fhir-packages", logger);
var loader = new NpmPackageLoader(httpClient, cacheManager, null, logger);

// Download a package
var packageStream = await loader.DownloadPackageAsync("hl7.fhir.us.core", "6.1.0", cancellationToken);

// Extract resources from package
var extractor = new PackageExtractor(logger);
var result = await extractor.ExtractAsync(packageStream, cancellationToken);

// Access extracted resources
foreach (var resource in result.Resources)
{
    Console.WriteLine($"{resource.ResourceType}: {resource.Canonical}");
}
```

## Package Loading

### NPM Registry

The `NpmPackageLoader` downloads packages from configurable NPM registries (default: https://packages.simplifier.net).

```csharp
// Download a package from registry
var loader = new NpmPackageLoader(httpClient, cacheManager, options, logger);
var packageStream = await loader.DownloadPackageAsync(
    "hl7.fhir.us.core",
    "6.1.0",
    cancellationToken
);
```

### Composite Loading

Use `CompositePackageLoader` to try multiple loaders in sequence (e.g., Embedded → NPM):

```csharp
var embeddedLoader = new EmbeddedPackageLoader(embeddedPackages, logger);
var npmLoader = new NpmPackageLoader(httpClient, cacheManager, null, logger);

var compositeLoader = new CompositePackageLoader(
    logger,
    embeddedLoader,  // Try built-in packages first
    npmLoader        // Fall back to NPM registry
);

var packageStream = await compositeLoader.DownloadPackageAsync(
    "hl7.fhir.us.core",
    "6.1.0",
    cancellationToken
);
```

## Package Discovery

### Search for Packages

```csharp
var searchService = new NpmPackageSearchService(httpClient, options, logger);

var results = await searchService.SearchPackagesAsync(
    "us core",
    maxResults: 10,
    cancellationToken
);

foreach (var result in results)
{
    Console.WriteLine($"Package: {result.PackageId}");
    Console.WriteLine($"  Latest: {result.LatestVersion}");
    Console.WriteLine($"  Description: {result.Description}");
    Console.WriteLine($"  Relevance: {result.RelevanceScore}%");
}
```

### Search Result

```csharp
public record PackageSearchResult
{
    // Package ID (e.g., "hl7.fhir.us.core")
    public required string PackageId { get; init; }
    
    // Package description
    public string? Description { get; init; }
    
    // FHIR version(s)
    public string? FhirVersion { get; init; }
    
    // Latest version
    public string? LatestVersion { get; init; }
    
    // Search relevance score (0-100)
    public int RelevanceScore { get; init; }
}
```

## Exact-Version, Verified Acquisition (Opt-in)

Use `NpmPackageAcquirer`, **not** the legacy Simplifier loader, for a standard NPM
selected-version metadata endpoint. Existing loader, search, permissive extractor
and cache APIs/callers are unchanged.

```csharp
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging;

var source = new NpmPackageSourcePolicy(
    "approved-source",
    new Uri("https://registry.example/npm/"),
    [new Uri("https://artifacts.example/packages/")]);
var extractor = new PackageExtractor(loggerFactory.CreateLogger<PackageExtractor>());
using var acquirer = new NpmPackageAcquirer(
    extractor,
    cache: new VerifiedPackageCache(Path.Combine(dataDirectory, "verified-packages")));

AcquiredNpmPackage artifact = await acquirer.AcquireAsync(
    new NpmPackageIdentity("@example/fhir-ig", "1.2.3-rc.1+build.7"),
    source,
    cancellationToken);

// Raw manifest and ALL other JSON files, including metadata-shaped entries.
string manifestJson = artifact.Extraction.Manifest.Json;
IReadOnlyList<StrictPackageEntry> entries = artifact.Extraction.JsonEntries;
```

### Identity and source trust

Identity construction trims boundary whitespace, requires lowercase ASCII scoped or
unscoped NPM names (1–214), and parses strict SemVer 2 versions (1–256). Uppercase names,
tags, ranges, partial versions, leading numeric zeroes and `v` prefixes fail. Prerelease
and build metadata remain exact ordinal identity; this library imposes no upgrade policy.
Names use either one segment or `@scope/name`: each segment starts with a lowercase
letter/digit, followed only by lowercase letters, digits, `.`, `_` or `-`.

The source ID is a separately administered, case-sensitive ASCII identifier (1–128).
Boundary whitespace is trimmed; its first character is a letter/digit, followed only
by ASCII letters, digits, `.`, `_` or `-`.
It is not a URL or credential. The registry and a nonempty list of artifact prefixes must
be explicit absolute HTTPS directories ending `/`. Authorization compares scheme,
normalized host, port and ordinal canonical directory containment, rejecting sibling-prefix matches.
Directory prefixes remain query/fragment-free. Artifact and redirect URLs support
valid UTF-8 escaped paths and opaque ASCII queries, including signed URLs.
Paths are decoded for authorization, but the original escaped path and query are
preserved on the wire and in the authentication callback: escape case, `+`, parameter
order, duplicate keys and empty values are not rewritten. Queries are not interpreted
as paths and never enter cache identity or acquisition diagnostics.
The callback URI disables .NET path/query canonicalization; use `AbsolutePath` and
`PathAndQuery` rather than `GetComponents` for path/query access.

Userinfo, fragments, controls, raw whitespace/backslashes, malformed percent escapes
or UTF-8, dot segments, repeated slashes and encoded path separators/delimiters fail.
Nested path escaping (`%25`, including double-encoded traversal) is unsupported.
Non-ASCII path/query characters must be percent-escaped. Safe escaped prefix segments
are allowed; canonical paths compare ordinally without Unicode normalization.

The locally generated exact metadata request has a separate controlled encoding rule:
`https://registry.example/npm/%40example%2Ffhir-ig/1.2.3-rc.1%2Bbuild.7`.
The scoped name remains one identifier under the nonroot registry prefix. Untrusted
artifact/redirect URLs do not inherit that exception. The response must match the exact
name/version and provide an authorized `dist.tarball`. No dist-tag/latest fallback,
public registry fallback or transitive dependency installation occurs.

### Transport, redirects and authentication

The acquirer privately constructs and owns its `SocketsHttpHandler`, with redirects,
cookies, ambient credentials, preauthentication, proxy use and automatic decompression
disabled. Public callers cannot inject or mutate the transport. All redirects are explicit, cycle-detected and
re-authorized **before** a request or authentication callback. `maxRedirects` defaults to
zero and applies per metadata/artifact chain per attempt. Metadata redirects stay inside
the registry prefix; artifact redirects stay inside configured artifact prefixes.

For private authentication integration, the constructor accepts
`Func<string, Uri, CancellationToken, ValueTask<AuthenticationHeaderValue?>> authenticate`.
The callback receives source ID and approved destination for each fresh request.
It must select credentials for that source **and destination**, never blindly forward
registry credentials to another allowlisted origin. Only the returned Authorization
header is applied; cookies are unsupported. Authentication failure is permanent and
sanitized, including an `OperationCanceledException` unrelated to the supplied token.
Actual caller or deadline cancellation propagates; the authenticator must honor its token.

The optional `validateServerCertificate` is a `RemoteCertificateValidationCallback`,
not a handler-configuration hook. It changes only TLS server trust, for example a
private-root or exact-certificate policy, and is independent of package intent.
Without it, ordinary system certificate validation applies. It cannot enable hidden
HTTP redirects, credential challenge replay, cookies or decompression.
Arbitrary HttpClient/HttpMessageHandler injection is deliberately absent from the
public acquisition API; the library's internal friend-test seam is not a production
configuration option. Checking only a final response URI would be too late.
The acquirer owns/disposes its HttpClient and transport. Extractor, authenticator,
clock and cache remain caller-owned. Requests, responses and body streams are disposed
on all success/failure paths.
Reuse the acquirer for concurrent operations only with concurrency-safe authentication
and certificate-validation callbacks. Do not dispose the acquirer while calls are active.

Metadata and tarball requests send `Accept-Encoding: identity`. Every original
`Content-Encoding` field and list member must declare identity: non-identity,
malformed and empty members are rejected before body reads, not silently decompressed.
The `.tgz` body itself is still gzip data whose **compressed bytes** are authenticated.

### Bounds, digest and deadline

The policy accepts `PackageExtractionLimits` plus `maxMetadataBytes` (default 1,048,576).
Extraction defaults are 33,554,432 compressed bytes; 268,435,456 expanded tar bytes including
overhead; 16,777,216 per entry; 10,000 physical entries; JSON depth 64. All are inclusive.
Actual bytes, not Content-Length, enforce limits. Declared oversize fails before reading;
unknown/chunked and misleading short lengths are counted. Identity-only HTTP encoding
means the metadata cap directly bounds decoded bytes. JSON depth also applies to metadata.

Exactly one canonical SHA-512 SRI token (`sha512-` plus canonical base64 of 64 bytes) is
required from selected-version metadata or optional administrator `integrityPin`.
Missing integrity is accepted only with a pin; present invalid or conflicting metadata
integrity fails. No SHA-1/shasum fallback or self-asserted locally calculated expected
digest. Verification uses the exact compressed tarball bytes and precedes strict extraction.
Strict manifest name/version must then equal requested identity.

`PackageAcquisitionRetryPolicy` defaults to 3 total attempts, 120-second per-attempt and
300-second overall timeouts, 1-second initial and 30-second maximum full-jitter exponential
backoff ceilings. All attempts share the overall deadline, including body reads,
digest verification, strict extraction and cache work. Backoff is outside the attempt
timeout but inside the overall deadline. Optional `TimeProvider` controls timers/time.

Only transient transport/timeouts and HTTP 408/429/500/502/503/504 retry. TLS authentication
or certificate rejection, including any exception thrown by the certificate-validation
callback, is permanent sanitized `TransportFailure`. Actual caller/deadline cancellation
is preserved; other connection failures remain transport-retry candidates.
Retry-After must have exactly one complete valid delta/date field (a date's comma is
part of that value, not a separator). Its delay
must fit both maximum delay and remaining deadline; invalid or excessive values stop the
call rather than being ignored or retried early. A delay equal to all remaining time
cannot leave time for another attempt and is rejected. A delay equal to the configured
maximum delay is accepted when remaining time is greater.

Trust, auth/not-found, metadata identity, integrity, digest, extraction and limit failures
do not retry. `PackageExtractionException : IOException` remains permanent. Internal
timeouts produce `PackageAcquisitionException` with `Error == Timeout`; caller
cancellation remains `OperationCanceledException` with the caller token.
Safe acquisition diagnostics expose `Error` and optional HTTP `StatusCode`, not
response bodies, URLs, credential-bearing inner exceptions or cache paths.

Timeouts are cooperative: synchronous hashing, extraction parser calls and filesystem
operations cannot be preempted mid-call; checks before/after stages prevent expired
success. In-memory byte bounds are not peak heap, concurrency or process CPU limits.

### Verified cache and host integration

`VerifiedPackageCache(directory)` is optional. Opaque SHA-256 filenames bind source ID,
exact name, exact version and expected SHA-512 integrity. Every call still obtains trusted
metadata and validates active URL policy. Cache hits repeat compressed bounds, digest,
strict extraction, manifest identity and all active extraction limits.
Corruption or filesystem errors fail explicitly without a silent network fallback.

Publication uses a unique same-directory staging file and atomic create-only rename
after verification. A concurrent winner is bounded-reread and compared against verified
bytes; partial artifacts are never published. Normal failure/cancellation cleans only
the owned staging file; no broad recursive cleanup occurs. A crash may leave an orphan
staging file, which is never read as an artifact. Protect this directory from untrusted
local writers; cross-process eviction, crash recovery and installed inventory are host
concerns. Acquisition makes no claim that a package is installed or activated.
Cleanup-only failure remains permanent `CacheFailure`, including staging-stream disposal.
If a buffered disposal flush or file cleanup also fails while
another failure is in flight, the primary error (including the caller cancellation
token or deadline timeout) is retained and retries stop. The primary exception's
`Data[PackageAcquisitionException.CacheCleanupFailureDataKey]` is set to
`PackageAcquisitionError.CacheFailure`. This sanitized secondary diagnostic exposes
neither a path nor a filesystem exception message. Hosts should observe it and handle
any owned staging orphan; cleanup never recursively deletes another caller's files.

The returned `AcquiredNpmPackage` exposes `SourceId`, `Identity`, canonical verified
`Integrity`, and the complete `StrictPackageExtractionResult`. Original dependencies,
provenance and singular/plural FHIR declarations remain raw. Adapters must explicitly
classify named package metadata and validate arbitrary metadata-shaped JSON; do not
blanket-discard null `ResourceType` entries. The generic library has no
SearchParameter-specific import, compatibility or activation policy and never runs scripts.

Local consumers can use project references or a coherently pinned local Ignixa package
closure matching net9/net10. Map host `long` byte limits by checked conversion and reject
values beyond the supported positive `int` capacity. Source registration, durable status,
SQL, installed inventory and activation belong to the host, not this library.

The library's developer tests exercise real loopback HTTPS through the production
transport. Windows runs the isolated TLS fixture using an existing `node` on PATH,
passing ephemeral certificate/key material through memory pipes because Schannel
cannot serve an ephemeral private key. This is a test-only prerequisite, not a
production dependency or certificate-store modification. Non-Windows uses SslStream.
Trust-rejection tests explicitly permit expected TLS handshake failures; other tests
fail on unexpected handshake/process errors with sanitized error codes. Unrestricted
stderr and certificate/key material are never emitted as fixture diagnostics.
The fixture exercises truncated content-length/chunked bodies, stalled final chunks
and concurrent acquisitions; shutdown cannot suppress a previously observed unexpected
handshake failure.

## Resource Extraction

### Extract from Package Stream

```csharp
var extractor = new PackageExtractor(logger);
var extractionResult = await extractor.ExtractAsync(packageStream, cancellationToken);

var manifest = extractionResult.Manifest;
var resources = extractionResult.Resources;

Console.WriteLine($"Package: {manifest.Name}@{manifest.Version}");
Console.WriteLine($"FHIR Version: {manifest.FhirVersion}");
Console.WriteLine($"Resources: {resources.Count}");
```

### Extracted Resources

```csharp
public record ExtractedResource
{
    // Resource type (e.g., "StructureDefinition")
    public required string ResourceType { get; init; }
    
    // Canonical URL
    public required string Canonical { get; init; }
    
    // Resource version
    public string? Version { get; init; }
    
    // Resource ID
    public required string ResourceId { get; init; }
    
    // Full JSON
    public required string ResourceJson { get; init; }
    
    // FHIR version
    public required string FhirVersion { get; init; }
}
```

### Strict, Bounded Extraction (Opt-in)

`PackageExtractor.ExtractStrictAsync(Stream, PackageExtractionLimits, CancellationToken)`
is additive. It does not change `IPackageExtractor`, its permissive `ExtractAsync` method,
or existing result models and callers.

```csharp
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;

var extractor = new PackageExtractor(loggerFactory.CreateLogger<PackageExtractor>());
var limits = new PackageExtractionLimits(
    maxCompressedBytes: 32 * 1024 * 1024,
    maxExpandedBytes: 256 * 1024 * 1024,
    maxEntryBytes: 16 * 1024 * 1024,
    maxArchiveEntries: 10_000,
    maxJsonDepth: 64);

StrictPackageExtractionResult result =
    await extractor.ExtractStrictAsync(packageStream, limits, cancellationToken);

int searchParameters = result.JsonEntries.Count(e => e.ResourceType == "SearchParameter");
int otherResources = result.JsonEntries.Count(e =>
    e.ResourceType is not null && e.ResourceType != "SearchParameter");
int metadataFiles = result.JsonEntries.Count(e => e.ResourceType is null);
```

The host selects and validates the resource types it imports; the extractor does not activate
FHIR resources, validate SearchParameter identity, or execute NPM scripts.

#### Limits, counting and stream lifetime

All five constructor arguments are independently configurable positive `int` maxima, immutable
after construction. Equality with a maximum is accepted; one over fails the entire operation.
The defaults align with the initial OSS policy; deployments must still validate package fit
and memory budgets:

| Limit | Default | Counting rule |
|---|---:|---|
| `MaxCompressedBytes` | 32 MiB (33,554,432 bytes) | Actual bytes read from current input position through EOF, including the gzip envelope |
| `MaxExpandedBytes` | 256 MiB (268,435,456 bytes) | All decompressed tar bytes: headers, metadata, ignored files, padding and end/record blocks |
| `MaxEntryBytes` | 16 MiB (16,777,216 bytes) | Every physical entry's payload, including hidden metadata and non-JSON files |
| `MaxArchiveEntries` | 10,000 | Physical nonzero tar headers, including local Pax/GNU metadata, directories and ignored files |
| `MaxJsonDepth` | 64 | Object/array nesting for each JSON file, including the manifest and metadata; outer container is depth one |

Byte limits use positive `int` values because this API buffers archives in memory. An OSS byte
policy represented as `long` must be validated within `1..Int32.MaxValue` and converted with
`checked((int)value)` at the host boundary. Reject values above `Int32.MaxValue` (2,147,483,647)
as invalid configuration; do not truncate, clamp or use unchecked casts. Policy conversion
belongs to the caller, not to a new overload of this extraction API.

`Statistics` reports actual `CompressedBytes`, `ExpandedBytes`, `PayloadBytes`,
`PhysicalEntries` and `LogicalEntries`. Payload bytes sum all physical entry bodies, excluding
their headers/padding. Logical entries are the resolved files/directories returned by the tar
reader; they include the manifest and ignored non-JSON files, but exclude hidden metadata
headers. Physical count therefore also bounds logical count.

The caller owns the stream. Reading is asynchronous, cancellation-aware, starts at its current
position, and never queries its `Length` or `Position`; non-seekable streams are supported.
The input remains open on success, validation failure and cancellation. Compressed reads stop
at most one byte beyond the configured maximum (needed to distinguish exact-limit EOF).
Cancellation is checked during input reads, inflation, framing validation, archive traversal,
entry reads, JSON token validation and before returning.
Pending input reads receive the cancellation token; the caller's stream must honor it.
Synchronous CPU/library operations are cooperatively cancellable between checkpoints, not
preemptible mid-call. This API does not impose an acquisition deadline.

Extraction is **in memory**, not streaming delivery of partially trusted resources. Compressed
and expanded buffers, individual JSON payloads, parser state and returned strings can coexist.
Byte limits are not peak-memory limits. Budget concurrency and heap headroom separately;
very large archives may require different limits or a different delivery strategy.
No extracted paths are written to disk.

#### Raw entries and manifest metadata

The result contains a `StrictPackageManifest`, `IReadOnlyList<StrictPackageEntry> JsonEntries`,
and `PackageExtractionStatistics`. Each entry has `Path`, original UTF-8-decoded `Json`, and
nullable `ResourceType`. Every non-manifest `.json` file (case-insensitive extension) is retained:

- A nonempty string `resourceType` identifies a resource candidate, including types outside
  the legacy conformance allowlist. Unknown resource-type strings remain for host validation.
- An absent `resourceType`, or a JSON root other than an object, denotes metadata JSON.
  JSON null/arrays/scalars are therefore retained as metadata, not counted as FHIR resources.
- A present non-string/blank `resourceType`, malformed JSON, invalid Unicode or duplicate JSON
  properties is an explicit failure. No entry is silently skipped for a JSON error.
- Canonical URL, `id`, resource version and other fields remain in the original JSON.
  Missing/invalid SearchParameter identity is never dropped or replaced by fabricated values.

Exactly one manifest at **`package/package.json`** is required, independent of tar order.
`Name` and `Version` must be nonblank strings; this is not a SemVer/source identity validator.
`FhirVersion` preserves singular `fhirVersion` or null when absent. `FhirVersions` preserves
the plural string array, in declared order, or an empty list when absent. Both can coexist;
conflicts and compatibility with a running FHIR version are host policy. `Json` retains the
entire manifest, including dependencies and the distinction between absent and empty arrays.
There is **no strict version fallback**. The unchanged permissive API still defaults missing
singular metadata to R4 `4.0.1`; callers opting into strict mode must choose any compatibility
fallback explicitly, rather than assigning resources a version based on archive ordering.

These public records are output DTOs. Direct construction does not perform extraction or FHIR
validation. The extractor supplies read-only collection backing; record equality does not
provide structural list equality, validated provenance or cryptographic artifact identity.

#### Archive and path policy

Supported archives contain one complete gzip member with verified header (including optional
header CRC), deflate termination, payload CRC and size footer. Missing/truncated data, another
gzip member, or any compressed trailing bytes fail. Tar requires two complete zero end blocks;
additional complete zero-filled record blocks are accepted and counted. Nonzero entry padding,
incomplete blocks, nonzero trailing data, and additional archives after the end marker fail.

Regular V7/USTAR/GNU/Pax files and directories are supported. Before the BCL `TarReader`
processes hidden metadata, a bounded framing gate validates physical checksums, sizes, counts,
payload/padding boundaries and supported types. Local Pax attributes and GNU long-path
metadata are allowed; a metadata header must precede a file/directory, not another metadata
header or EOF. Global Pax, sparse files, GNU long-link metadata, links, devices, FIFOs and
other special types are rejected. Pax `size`, `linkpath`, `hdrcharset`, `GNU.sparse*` and
`SCHILY.*` attributes are deliberately unsupported so they cannot change the validated layout.
Duplicate or malformed Pax attributes fail. Embedded LF, CR or NUL within an attribute is
rejected because supported BCL runtimes parse record boundaries differently. Numeric sizes
accept octal digits with optional leading spaces and trailing NUL/space padding, or positive
GNU base-256; digits after a NUL terminator, negative/overflowing binary sizes and binary
checksums are rejected. The same unambiguous octal grammar also applies to mode, user/group
IDs and timestamps, including hidden metadata. Signed base-256 IDs must fit signed 32-bit
values; timestamps (including ordinary negative historical values) must fit the
`DateTimeOffset` Unix-seconds range. GNU access/change timestamps are checked whenever
the reader consumes GNU attributes, including a header inheriting its format from
long-path metadata. Device types are rejected before device numbers can be consumed;
unused device/GNU auxiliary fields in regular files impose no additional numeric policy.
GNU long paths may include or omit their final NUL, with the actual
physical payload size counting toward limits in either case.

Opaque metadata-header names are not extraction paths. A narrow framing map preserves the
effective UTF-8 wire name, including USTAR prefixes and local Pax/GNU path overrides, without
trimming spaces. NUL-terminated name fields cannot hide nonzero bytes after the terminator.
The effective spelling is path-validated before BCL name normalization. Every file/directory
returned by `TarReader` must match the mapped physical data offset, payload size, type and
exact effective name; early termination, skipped entries, extra entries or disagreement fail.
This reconciliation includes ignored non-JSON files as well as retained JSON and manifests.

All logical paths must use `/` separators under the exact `package` root. Directories may have
one trailing slash, which is removed for identity comparisons. Empty/traversal/dot segments,
rooted/drive/UNC paths, backslashes, control characters, colons, trailing segment dots/spaces,
and invalid UTF-8 replacement characters are rejected. No URL decoding or Unicode
normalization is performed. Duplicate full paths are compared ordinally, case-insensitively;
file/directory and file/ancestor collisions also fail. Directory ancestors may be implicit.
Misplaced/nested manifests whose leaf is exactly `package.json` (case-insensitively), including
cased canonical-path aliases, fail instead of using last-one-wins. Longer leaves such as
`SearchParameter-package.json` and `notpackage.json` are ordinary JSON entries, not manifests.

#### Failure contract

`PackageExtractionException : IOException` carries a `PackageExtractionDiagnostic`:
`Code`, optional one-based **physical** `EntryIndex`, and optional configured `Limit`.
Indices always include hidden metadata headers. Resolved-path/JSON validation errors identify
the file/directory's data header. Metadata decoding failures identify the metadata header carrying
the malformed bytes; for example, invalid UTF-8 in GNU long-path metadata yields `UnsafePath`
at that metadata header. Framing errors identify the physical header being validated.
Reader disagreement identifies the expected physical entry. An incomplete/trailing
archive can identify the next expected index. Archive-wide failures may omit the index. Codes are:

- `CompressedSizeLimit`, `ExpandedSizeLimit`, `EntrySizeLimit`, `EntryCountLimit`, `JsonDepthLimit`
- `InvalidArchive`, `UnsafePath`, `DuplicatePath`, `UnsupportedEntryType`
- `MissingManifest`, `DuplicateManifest`, `InvalidManifest`, `InvalidJson`

Messages/diagnostics contain no package JSON, attacker-controlled filenames or parser messages,
and this strict path does not log package content. Failure never returns a partial result.
Cancellation remains `OperationCanceledException`; null/unreadable input or null limits are
argument errors. Underlying caller-stream I/O failures propagate rather than being disguised
as archive validation success.

This API establishes a generic extraction boundary, not source trust, cryptographic artifact
identity, acquisition/registry behavior, installation state or server ownership/activation.

## Caching

### Configure Cache Location

```csharp
var options = new NpmPackageLoaderOptions
{
    RegistryUrl = "https://packages.simplifier.net",
    RequestTimeout = TimeSpan.FromSeconds(30),
    EnableRetryPolicies = true
};

var cacheManager = new PackageCacheManager("/var/cache/fhir-packages", logger);
var loader = new NpmPackageLoader(httpClient, cacheManager, options, logger);
```

### Cache Manager

```csharp
var cacheManager = new PackageCacheManager(cacheDirectory, logger);

// Get cache path for a package
var cachePath = cacheManager.GetCachePath("hl7.fhir.us.core", "6.1.0");

// Check if package is cached
bool isCached = cacheManager.IsCached("hl7.fhir.us.core", "6.1.0");

// Delete cached package
cacheManager.DeleteCachedPackage("hl7.fhir.us.core", "6.1.0");

// Clear entire cache
cacheManager.ClearCache();
```

## Offline Mode

```csharp
var options = new NpmPackageLoaderOptions
{
    RegistryUrl = "https://packages.simplifier.net",
    EnableRetryPolicies = false
};

var cacheManager = new PackageCacheManager(offlineCacheDirectory, logger);

// Use only cached packages - no network requests
var loader = new NpmPackageLoader(httpClient, cacheManager, options, logger);
```

## Registry Configuration

### Custom Registry

```csharp
var options = new NpmPackageLoaderOptions
{
    RegistryUrl = "https://my-internal-registry.example.org"
};

var loader = new NpmPackageLoader(httpClient, cacheManager, options, logger);
```

### Retry Policies

```csharp
// Built-in resilience policies for transient failures
var retryPolicy = PackageLoaderResiliencePolicies.CreateRetryPolicy(logger);
var circuitBreakerPolicy = PackageLoaderResiliencePolicies.CreateCircuitBreakerPolicy(logger);

// Configure resilient HTTP handler
var handler = new ResilientHttpMessageHandler(new HttpClientHandler(), logger);
var httpClient = new HttpClient(handler);
```

## Package Manifest

### PackageManifest Record

```csharp
public record PackageManifest
{
    // Package name (e.g., "hl7.fhir.us.core")
    public required string Name { get; init; }
    
    // Package version (e.g., "5.0.1")
    public required string Version { get; init; }
    
    // FHIR version (e.g., "4.0.1")
    public required string FhirVersion { get; init; }
    
    // Package title
    public string? Title { get; init; }
    
    // Package description
    public string? Description { get; init; }
    
    // License
    public string? License { get; init; }
}
```

## Known Packages

```csharp
// Core FHIR packages (pre-compiled, should not load at runtime)
if (KnownPackages.IsCorePackage("hl7.fhir.r4.core"))
{
    // Skip loading - use embedded Ignixa.Specification instead
}

// Examples of core packages:
// - hl7.fhir.r2.core
// - hl7.fhir.r3.core
// - hl7.fhir.r4.core
// - hl7.fhir.r4b.core
// - hl7.fhir.r5.core
```

## Related Documentation

- [Validation](/docs/core-sdk/validation)
- [Core SDK Overview](/docs/core-sdk/overview)

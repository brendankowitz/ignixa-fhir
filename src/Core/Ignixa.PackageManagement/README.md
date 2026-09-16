# Ignixa.PackageManagement

NPM package management for FHIR Implementation Guides. Handles downloading, caching, and loading FHIR packages from NPM registries like packages.fhir.org.

## Why Use This Package?

- **Download FHIR packages**: Fetch Implementation Guides and core spec packages from NPM registries
- **Local caching**: Automatically cache downloaded packages to avoid re-downloads
- **Multiple sources**: Support for NPM registry, embedded packages, and custom loaders
- **Resource extraction**: Extract StructureDefinitions, ValueSets, and other conformance resources from packages

## Installation

```bash
dotnet add package Ignixa.PackageManagement
```

## Quick Start

### Exact-Version Acquisition from a Standard NPM Registry (Opt-in)

`NpmPackageAcquirer` is separate from the legacy `NpmPackageLoader` and its
Simplifier direct-download protocol. It reads the selected version's NPM metadata,
authorizes `dist.tarball`, verifies SHA-512 over the compressed bytes, and calls
the strict extractor before returning or caching anything.

```csharp
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;

var extractor = new PackageExtractor(loggerFactory.CreateLogger<PackageExtractor>());
var source = new NpmPackageSourcePolicy(
    sourceId: "approved-registry",
    registryBaseUri: new Uri("https://registry.example/npm/"),
    allowedArtifactPrefixes: [new Uri("https://artifacts.example/packages/")]);
var cache = new VerifiedPackageCache(Path.Combine(dataDirectory, "verified-packages"));

using var acquirer = new NpmPackageAcquirer(extractor, cache: cache);
AcquiredNpmPackage acquired = await acquirer.AcquireAsync(
    new NpmPackageIdentity("@example/fhir-ig", "1.2.3-rc.1+build.7"),
    source,
    cancellationToken);

StrictPackageExtractionResult raw = acquired.Extraction;
string originalManifest = raw.Manifest.Json;
IReadOnlyList<StrictPackageEntry> allJson = raw.JsonEntries;
```

- Names are trimmed lowercase ASCII NPM names (1–214 characters), optionally
  `@scope/name`; each segment starts with a letter/digit and then contains only
  letters, digits, `.`, `_` or `-`. Uppercase is rejected. Versions are trimmed strict SemVer 2 strings
  (1–256), never tags/ranges/partial versions. Build metadata is exact identity.
- Source IDs are trimmed, case-sensitive ASCII identifiers (1–128), starting with
  a letter/digit and then containing only letters, digits, `.`, `_` or `-`; no credentials
  or URLs in package intent. Registry and nonempty artifact prefixes must be
  absolute HTTPS directories with explicit trailing `/`. Every redirect is
  manually re-authorized by scheme, normalized host, port and ordinal path.
  Zero redirects are allowed by default; cycles fail. No public-feed fallback.
- Directory prefixes cannot contain queries or fragments. Artifact and redirect URLs
  support valid UTF-8 escaped paths and opaque ASCII queries, including signed URLs.
  Authorization compares decoded canonical directory paths; requests/auth callbacks
  preserve exact escaped path/query spelling, including escape case, `+`, duplicates
  and parameter order. Queries never enter cache identity or acquisition diagnostics.
  Callback URIs disable .NET path/query canonicalization to retain that spelling;
  use `AbsolutePath`/`PathAndQuery`, not `GetComponents`, for those components.
  Userinfo, fragments, controls, raw whitespace/backslashes, malformed escapes/UTF-8,
  dot segments, repeated slashes, encoded path separators/delimiters and nested path
  escaping (`%25`) are rejected. Escape non-ASCII query values; query values are not
  interpreted as paths. The locally constructed exact metadata request separately
  escapes the validated scoped name as **one** identifier and the exact version.
- The production transport is privately constructed and owned. It enforces disabled
  automatic redirects, cookies, ambient credentials, preauthentication, proxy use
  and automatic decompression; callers cannot supply or mutate its HttpClient/handler.
  Optional `validateServerCertificate` (`RemoteCertificateValidationCallback`) changes
  only TLS server trust, for example an administrator's private-root or exact-certificate
  policy. Omitting it retains system certificate validation. It cannot change HTTP
  redirects, credentials, cookies or decoding. Arbitrary handlers exist only in the
  internal friend-test seam, not the public API.
- Optional `authenticate` has type
  `Func<string, Uri, CancellationToken, ValueTask<AuthenticationHeaderValue?>>`.
  It is called for each approved source/destination, including same-origin redirects.
  Bind credentials to the source **and destination origin/path**, not just the package
  or allowlist. No Authorization header is copied from the preceding request.
  Cookies are never supported. Authenticator exceptions, including cancellation
  unrelated to the supplied token, become safe permanent authentication diagnostics.
  Actual caller/deadline cancellation propagates; the authenticator must honor its token.
  Reuse the acquirer; callbacks must support concurrent calls. Do not dispose it while
  acquisitions are active. The acquirer owns its client/transport, not the extractor,
  authenticator, certificate callback, clock or cache.
- Both metadata and tarballs request `Accept-Encoding: identity`. HTTP content
  encodings other than identity are rejected **before reading**; gzip tarballs
  themselves remain ordinary compressed artifact bytes. Thus automatic HTTP
  decompression cannot bypass the decoded metadata cap.
- Defaults: 32 MiB compressed, 256 MiB expanded including all tar overhead,
  16 MiB per entry, 10,000 physical entries, JSON depth 64, and 1 MiB metadata.
  `PackageExtractionLimits`, `maxMetadataBytes`, `PackageAcquisitionRetryPolicy`,
  and `maxRedirects` configure these bounds. Actual streamed bytes are counted;
  a misleading smaller Content-Length cannot bypass limits. Metadata and cache
  rereads are bounded too. Memory/concurrency budgeting remains the host's job.
- Exactly one canonical `sha512-<base64 of 64 bytes>` integrity token is required.
  `integrityPin` optionally supplies an administrator's `NpmPackageIntegrity`;
  absent metadata integrity is allowed only with that pin. A present malformed
  integrity or a pin/metadata disagreement fails, never downgrades to SHA-1.
- Three **total attempts**, 120 seconds each, share a 300-second overall deadline.
  Full-jitter exponential backoff starts at a 1-second ceiling, capped at 30 seconds.
  Only transient transport/timeouts and HTTP 408/429/500/502/503/504 retry.
  TLS authentication/certificate rejection is permanent sanitized `TransportFailure`;
  it does not retry. Retry-After is
  honored only if valid and within the maximum delay and remaining deadline;
  otherwise acquisition stops rather than retrying early. Body reads, digest,
  extraction and cache work all participate. Inject `TimeProvider` for deterministic
  time control. CPU/filesystem calls are cooperative, not forcibly preemptible.
- `PackageAcquisitionException.Error` and optional `StatusCode` are safe diagnostics
  without nested transport messages, URLs, response bodies or credentials. Timeout
  is distinct from permanent failures; caller cancellation remains cancellation.
  Strict `PackageExtractionException` is permanent **even though it derives from
  IOException**. Cache I/O/corruption errors are explicit; no success-shaped fallback.
- The optional filesystem cache uses SHA-256 opaque filenames derived from source,
  exact name/version and expected SHA-512 digest. Every call still resolves current
  trusted metadata and policy; hits repeat compressed bounds, digest and strict
  extraction/identity checks. Publication is atomic and create-only. Concurrent
  winners are reread and compared; only the caller's unique staging file is cleaned
  up. Protect the cache directory from untrusted local writers. This is not installed
  inventory, durable activation state, an offline registry or a cache eviction service.
  Cleanup-only failure is permanent `CacheFailure`. If cleanup also fails during a
  primary failure, that primary error/caller token is preserved and retries stop:
  `exception.Data[PackageAcquisitionException.CacheCleanupFailureDataKey]` contains
  `PackageAcquisitionError.CacheFailure`, never the path or nested filesystem message.
  A failed cleanup can leave an owned staging orphan for host-directed recovery.

The raw result includes every strict JSON entry and the original manifest, retaining
dependencies, provenance and singular/plural FHIR declarations. A null `ResourceType`
does **not** mean safe-to-ignore metadata: downstream adapters must explicitly classify
`package/package.json` and `package/.index.json` and validate remaining JSON according
to their own policy. The generic library neither installs transitive dependencies nor
executes scripts nor imports/activates resources.

For local SDK integration, reference this project or consume a coherently pinned
locally built Ignixa dependency closure for the consumer's net9/net10 target. Adapt
host `long` byte policies with checked conversion/rejection above `Int32.MaxValue`;
do not silently clamp to fit in-memory extraction. No host-specific source registration,
database, inventory or activation API is provided.

Developer verification includes real loopback HTTPS requests through the production
transport. On Windows, those tests require `node` on PATH because Schannel cannot
serve an ephemeral private key; the isolated test server receives certificate/key
material through memory pipes, never a trust store or key file. Node is **not** a
production package dependency. Non-Windows tests use in-process `SslStream`.

### Loading a Package from NPM

```csharp
using Ignixa.PackageManagement.Infrastructure;
using Microsoft.Extensions.Logging;

// Create HTTP client and logger
var httpClient = new HttpClient();
var logger = loggerFactory.CreateLogger<NpmPackageLoader>();

// Create package loader
var loader = new NpmPackageLoader(httpClient, logger);

// Download a package
var packageStream = await loader.LoadAsync(
    "hl7.fhir.us.core",
    "5.0.1",
    cancellationToken);

// packageStream contains the .tgz file
```

### Using Package Cache

```csharp
using Ignixa.PackageManagement.Infrastructure;

// Set up cache manager
var cacheDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "fhir-packages");
var cacheManager = new PackageCacheManager(cacheDirectory);

// Create loader with caching
var loader = new NpmPackageLoader(
    httpClient,
    cacheManager,
    options: null,
    logger);

// First call downloads from NPM
var stream1 = await loader.LoadAsync("hl7.fhir.r4.core", "4.0.1", cancellationToken);

// Second call uses cached version (instant)
var stream2 = await loader.LoadAsync("hl7.fhir.r4.core", "4.0.1", cancellationToken);
```

### Custom Registry Options

```csharp
using Ignixa.PackageManagement.Infrastructure;

// Use a custom NPM registry
var options = new NpmPackageLoaderOptions
{
    RegistryUrl = "https://my-custom-registry.org/"
};

var loader = new NpmPackageLoader(httpClient, cacheManager, options, logger);
```

### Extracting Package Contents

```csharp
using Ignixa.PackageManagement.Infrastructure;

var extractor = new PackageExtractor(loggerFactory.CreateLogger<PackageExtractor>());
var result = await extractor.ExtractAsync(
    packageStream,
    cancellationToken);

// In-memory manifest and permissively filtered conformance resources; no files are written.
Console.WriteLine($"{result.Manifest.Name}: {result.Resources.Count} resources");
```

### Opt-in Strict Extraction

Use the concrete extractor's additive strict method when consuming untrusted package archives.
The existing `IPackageExtractor.ExtractAsync` contract and permissive behavior are unchanged.

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

foreach (StrictPackageEntry entry in result.JsonEntries)
{
    // Null ResourceType denotes metadata JSON; other strings include all resource types.
    // The host decides which resources to validate/import and which to count as ignored.
    if (entry.ResourceType == "SearchParameter")
    {
        ValidateSearchParameter(entry.Json); // Your identity and FHIR-version validation.
    }
}
```

These configurable, inclusive defaults align with the initial OSS policy: 33,554,432 compressed
bytes, 268,435,456 expanded tar bytes, 16,777,216 bytes per physical entry, 10,000 physical entries
and JSON depth 64. Deployments must still validate package fit and memory budgets.
All limits are positive `int` values because extraction uses in-memory buffers. When adapting
an OSS byte policy stored as `long`, validate the range `1..Int32.MaxValue` and convert with
`checked((int)value)`. Reject values above `Int32.MaxValue` (2,147,483,647) as invalid configuration;
never truncate, clamp or use unchecked casts.

Compressed input is read asynchronously from the current position to EOF;
non-seekable inputs work and the caller's stream stays open on success, failure and cancellation.
Strict extraction buffers compressed and expanded data in memory, then retains JSON strings.
Limits bound archive bytes, **not peak process memory**; budget for buffers, parsing and concurrent
extractions. No files are written and no NPM scripts are run.
Cancellation is forwarded to pending input reads and checked between parsing operations;
input implementations must honor the token. Synchronous CPU parsing is cooperative, not preemptible.

- Expanded bytes include the entire tar: headers, hidden Pax/GNU metadata, skipped payloads,
  padding, two end blocks and any additional zero-filled record blocks.
- Entry bytes include every physical payload, even metadata and ignored non-JSON files.
  Archive entry count is **physical headers**, including metadata, directories and ignored files.
  The result also reports logical entries, payload bytes and actual compressed/expanded bytes.
- JSON depth counts the outer object/array as one, independently for every JSON file.
  Malformed JSON, invalid Unicode, duplicate properties and invalid `resourceType` values fail.
- Exactly `package/package.json` is required once. Paths are relative POSIX paths below
  `package/`; traversal, backslashes, case-insensitive collisions and links are rejected.
  Benign directories are permitted. A leaf equal to `package.json` (case-insensitively) at
  another path is rejected; longer filenames such as `SearchParameter-package.json` are ordinary JSON.
- `Manifest.FhirVersion` preserves the declared singular value or null; `FhirVersions`
  preserves the declared array (empty when absent). Neither sets a resource's running version.
  Original manifest JSON is retained, including other metadata. There is **no strict R4 fallback**;
  the existing permissive method retains its historical `4.0.1` default.
- `JsonEntries` preserves non-manifest JSON and resolved archive paths without requiring,
  inventing or filtering canonical URLs or logical IDs. Missing SearchParameter identity is
  left for downstream validation. No resources are activated by this API.
- Failure throws `PackageExtractionException` with a content-free `Diagnostic` (code,
  optional one-based **physical** header index and configured limit); no partial result is returned. Cancellation
  remains `OperationCanceledException`. Caller I/O failures are not disguised as validation.

Strict format support is intentionally narrower than general-purpose tar tools: one complete
gzip member with verified CRC/size and no trailing compressed data; regular POSIX/GNU files
and directories; local Pax metadata and GNU long paths bounded before `TarReader` sees them.
Global Pax metadata, sparse formats, long-link metadata, Pax structural size/link/charset
overrides and special files are rejected. Ambiguous numeric fields and Pax records containing
embedded newline, carriage return or NUL fail. Effective UTF-8 wire paths (including USTAR prefixes
and Pax/GNU overrides) are validated before reader trimming, and every reader entry is reconciled
with its physical offset, size, type and exact path. No ignored body can hide a following entry.
Nonzero tar padding or trailing archives/data fail. The result records are output DTOs, not
validation factories; their equality does not establish structural list equality or artifact identity.
See the [package-management documentation](https://brendankowitz.github.io/ignixa-fhir/docs/core-sdk/package-management)
for the exact strict boundary. Source trust, acquisition, digest verification and server policy
remain separate concerns.

### Loading Resources from Package

```csharp
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.Abstractions;

// Create resource provider
var resourceProvider = new PackageResourceProvider(
    packageDirectory,
    logger);

// Get all StructureDefinitions
var structureDefinitions = resourceProvider.GetResources("StructureDefinition");

foreach (var sd in structureDefinitions)
{
    Console.WriteLine($"Loaded {sd.Name}: {sd.Url}");
}
```

## Common Package IDs

```csharp
// US Core Implementation Guides
"hl7.fhir.us.core"     - US Core IG (v5.0.1, v6.1.0, etc.)

// International Patient Summary
"hl7.fhir.uv.ips"      - IPS IG

// Other Common IGs
"hl7.fhir.us.carin-bb" - CARIN Blue Button
"hl7.fhir.us.davinci-pdex" - Da Vinci Payer Data Exchange
```

## Advanced Usage

### Composite Package Loader

Load from multiple sources with fallback:

```csharp
using Ignixa.PackageManagement.Infrastructure;

// Combine embedded + NPM loaders
var embeddedLoader = new EmbeddedPackageLoader();
var npmLoader = new NpmPackageLoader(httpClient, cacheManager, options, logger);

var compositeLoader = new CompositePackageLoader(
    embeddedLoader,  // Try embedded first (fast)
    npmLoader        // Fall back to NPM (slow)
);

// Will check embedded first, then NPM
var stream = await compositeLoader.LoadAsync("hl7.fhir.r4.core", "4.0.1", cancellationToken);
```

### Searching for Packages

```csharp
using Ignixa.PackageManagement.Infrastructure;

var searchService = new NpmPackageSearchService(httpClient, logger);

// Search for packages
var results = await searchService.SearchAsync("us-core", cancellationToken);

foreach (var result in results)
{
    Console.WriteLine($"{result.Name} - {result.Description}");
}

// Get package metadata
var metadata = await searchService.GetPackageMetadataAsync(
    "hl7.fhir.us.core",
    cancellationToken);

Console.WriteLine($"Latest version: {metadata.DistTags.Latest}");
```

## Integration with Other Packages

This package is often used together with:

- **Ignixa.Specification**: Load packages to build custom schema providers
- **Ignixa.Validation**: Load profiles for validation
- **Ignixa.Search**: Load custom SearchParameter definitions

```csharp
// Example: Load US Core for validation
var loader = new NpmPackageLoader(httpClient, logger);
var stream = await loader.LoadAsync("hl7.fhir.us.core", "5.0.1", cancellationToken);

var extractor = new PackageExtractor();
var packageDir = await extractor.ExtractAsync(stream, tempPath, cancellationToken);

var resourceProvider = new PackageResourceProvider(packageDir, logger);
var profiles = resourceProvider.GetResources("StructureDefinition");

// Use profiles with Ignixa.Validation
```

## Package Cache Location

By default, packages are cached in:
- **Windows**: `%LOCALAPPDATA%\fhir-packages`
- **Linux/Mac**: `~/.local/share/fhir-packages`

You can override this by providing a custom `PackageCacheManager`.

## License

MIT License - see LICENSE file in repository root

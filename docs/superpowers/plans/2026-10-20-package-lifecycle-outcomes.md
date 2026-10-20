# Package Lifecycle Outcomes (ADR-2610 Slice 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every package-load boundary (SDK result, HTTP, MCP, preload) report structured dependency-closure and activation outcomes, and stop swallowing cancellation, without changing install semantics.

**Architecture:** Add a typed `PackageClosureMember` outcome list to `PackageImportResult`, populated by `ImplementationGuideProvider.LoadPackageWithDependenciesAsync` alongside the legacy string lists. `LoadPackageHandler` carries closure members plus the root `ActivationResult` into `LoadPackageResult`, which exposes a single `IsComplete` verdict. HTTP, MCP, and preload map that verdict instead of reporting unconditional success. An `IPackageActivationPipeline` seam makes the handler and MCP tool testable.

**Tech Stack:** .NET, Medino CQRS, ASP.NET Core Minimal APIs, Autofac, xUnit, Shouldly, NSubstitute.

**Spec:** `docs/features/package-management/adr-2610-reliable-package-lifecycle.md` and `docs/features/package-management/investigations/reliable-package-lifecycle.md` (section "Delivery order and acceptance evidence", slice 1).

## Global Constraints

- Dependency direction: API -> Application -> Domain <- DataLayer. No `Hl7.Fhir.*` in Application.
- Every async API accepts `CancellationToken cancellationToken`; caller cancellation must propagate.
- Never swallow exceptions or add success-shaped fallbacks.
- Tenant `0` is never exposed through `/tenant/0`.
- File-scoped namespaces, one type per file, `_camelCase` private fields, no `#region`.
- Tests: xUnit + Shouldly + NSubstitute, Arrange/Act/Assert, `GivenContext_WhenAction_ThenResult`.
- Only call a test a regression if it fails at the base commit; mutation-test guards.
- Do not commit without explicit user approval (`AGENTS.md`); each "Commit" step means "commit once approved".
- Out of scope for this plan: durable installation records, visibility gating, dependency planning, cycle rejection, verified acquisition, dependency activation ordering, CLI changes. These are slices 2-5 (see Roadmap).
- Compatibility: HTTP status codes and existing JSON fields stay unchanged; new fields are additive. `LoadedPackages`/`SkippedPackages` strings keep their current formats.

---

## File Structure

| File | Responsibility |
|---|---|
| Create `src/Application/Ignixa.Application/Features/Packages/PackageClosureMemberStatus.cs` | Enum of per-package closure outcomes (string-serialized). |
| Create `src/Application/Ignixa.Application/Features/Packages/PackageClosureMember.cs` | Typed outcome record with `IsBlocking`. |
| Modify `src/Application/Ignixa.Application/Features/Packages/PackageImportResult.cs` | Add `ClosureMembers`. |
| Modify `src/Application/Ignixa.Application/Features/Packages/ImplementationGuideProvider.cs:143-300` | Populate `ClosureMembers`; propagate cancellation from manifest re-fetch. |
| Create `src/Application/Ignixa.Application/Features/Conformance/IPackageActivationPipeline.cs` | Test seam over activation. |
| Modify `src/Application/Ignixa.Application/Features/Conformance/PackageActivationPipeline.cs:23` | Implement the interface. |
| Modify `src/Application/Ignixa.Api/Registrations/ConformanceServicesRegistration.cs:71-74` | Register interface. |
| Modify `src/Application/Ignixa.Application/Features/Admin/LoadPackageCommand.cs` | Add `ClosureMembers`, `Activation`, `IsComplete` to `LoadPackageResult`. |
| Modify `src/Application/Ignixa.Application/Features/Admin/LoadPackageHandler.cs` | Populate new result fields. |
| Modify `src/Application/Ignixa.Api/Endpoints/AdminPackageEndpoints.cs` | Map outcomes into `LoadPackageResponse`; propagate cancellation. |
| Modify `src/Application/Ignixa.Application/Features/Experimental/Mcp/Tools/PackageManagement/InstallPackageTool.cs` and `.../Mcp/Dtos/InstallPackageResultDto.cs` | Report activation failure instead of `Success = true`. |
| Modify `src/Application/Ignixa.Api/Services/TenantPackagePreloadService.cs:200-215` | Warn on incomplete loads. |
| Test `test/Ignixa.Application.Tests/Features/Packages/ImplementationGuideProviderClosureOutcomeTests.cs` (create) | Provider closure outcomes + cancellation regression. |
| Test `test/Ignixa.Application.Tests/Features/Admin/LoadPackageHandlerTests.cs` (create) | Handler outcome propagation. |
| Test `test/Ignixa.Api.Tests/Endpoints/AdminPackageEndpointLoadOutcomeTests.cs` (create) | HTTP response shape and cancellation. |
| Test `test/Ignixa.Api.Tests/Mcp/InstallPackageToolTests.cs` (create) | MCP activation-failure reporting. |
| Modify `docs/site/docs/server/fhir/operations.md:436-441`, `docs/site/docs/server/features/mcp-server.md:190-215` | Document new response fields. |

Validation CLI (`tools/Ignixa.Validation.Cli/Commands/ValidateCommand.cs:170-200`) needs no slice-1 change: it loads only the listed packages, fails fast with exit code 1, and rethrows cancellation. Dependency-aware CLI loading belongs to slice 4.

---

### Task 1: Typed closure outcomes in the provider

**Files:**
- Create: `src/Application/Ignixa.Application/Features/Packages/PackageClosureMemberStatus.cs`
- Create: `src/Application/Ignixa.Application/Features/Packages/PackageClosureMember.cs`
- Modify: `src/Application/Ignixa.Application/Features/Packages/PackageImportResult.cs`
- Modify: `src/Application/Ignixa.Application/Features/Packages/ImplementationGuideProvider.cs:143-300`
- Test: `test/Ignixa.Application.Tests/Features/Packages/ImplementationGuideProviderClosureOutcomeTests.cs`

**Interfaces:**
- Produces:
  - `enum PackageClosureMemberStatus { Imported, AlreadyInstalled, ProvidedInProcess, VersionConflict, LoadFailed, ManifestUnavailable }` in namespace `Ignixa.PackageManagement.Models`.
  - `sealed record PackageClosureMember(string PackageId, string Version, PackageClosureMemberStatus Status, string? Reason = null)` with `bool IsBlocking`.
  - `PackageImportResult.ClosureMembers : IReadOnlyList<PackageClosureMember>` (empty for single-package loads).
- Semantics: one entry per observation, so an already-installed package whose manifest cannot be re-read appears as both `AlreadyInstalled` and `ManifestUnavailable`. `ProvidedInProcess` is informational, not blocking.

- [ ] **Step 1: Write the failing tests**

Create `test/Ignixa.Application.Tests/Features/Packages/ImplementationGuideProviderClosureOutcomeTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Ignixa.Domain.Abstractions;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.Packages;

public sealed class ImplementationGuideProviderClosureOutcomeTests
{
    private readonly IPackageLoader _loader = Substitute.For<IPackageLoader>();
    private readonly IPackageExtractor _extractor = Substitute.For<IPackageExtractor>();
    private readonly IPackageResourceImporter _importer = Substitute.For<IPackageResourceImporter>();
    private readonly IPackageResourceRepository _repository = Substitute.For<IPackageResourceRepository>();
    private readonly ImplementationGuideProvider _provider;

    public ImplementationGuideProviderClosureOutcomeTests()
    {
        _provider = new ImplementationGuideProvider(
            _loader,
            _extractor,
            _importer,
            _repository,
            NullLogger<ImplementationGuideProvider>.Instance);

        _importer.ImportAsync(Arg.Any<PackageExtractionResult>(), _repository, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var extraction = call.Arg<PackageExtractionResult>();
                return Task.FromResult(new PackageImportResult
                {
                    PackageId = extraction.Manifest.Name,
                    PackageVersion = extraction.Manifest.Version,
                    TotalResources = 1,
                    ImportedResources = 1,
                });
            });
        _repository.PackageVersionExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(false);
    }

    [Fact]
    public async Task GivenImportedAndAlreadyInstalledMembers_WhenLoadingWithDependencies_ThenStatusesAreReportedAndClosureIsComplete()
    {
        RegisterPackage("root", "1.0.0", new Dictionary<string, string> { ["dep"] = "1.0.0" });
        RegisterPackage("dep", "1.0.0");
        _repository.PackageVersionExistsAsync("dep", "1.0.0", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        result.ClosureMembers.ShouldContain(new PackageClosureMember("root", "1.0.0", PackageClosureMemberStatus.Imported));
        result.ClosureMembers.ShouldContain(new PackageClosureMember("dep", "1.0.0", PackageClosureMemberStatus.AlreadyInstalled));
        result.ClosureMembers.ShouldAllBe(m => !m.IsBlocking);
    }

    [Fact]
    public async Task GivenCoreDependency_WhenLoadingWithDependencies_ThenProvidedInProcessIsNotBlocking()
    {
        RegisterPackage("root", "1.0.0", new Dictionary<string, string> { ["hl7.fhir.r4.core"] = "4.0.1" });

        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        var core = result.ClosureMembers.Single(m => m.PackageId == "hl7.fhir.r4.core");
        core.Status.ShouldBe(PackageClosureMemberStatus.ProvidedInProcess);
        core.IsBlocking.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenTransitiveLoadFailure_WhenLoadingWithDependencies_ThenMemberIsLoadFailedAndBlocking()
    {
        RegisterPackage("root", "1.0.0", new Dictionary<string, string> { ["broken"] = "2.0.0" });
        _loader.DownloadPackageAsync("broken", "2.0.0", Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ => throw new InvalidOperationException("tarball missing"));

        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        var broken = result.ClosureMembers.Single(m => m.PackageId == "broken");
        broken.Status.ShouldBe(PackageClosureMemberStatus.LoadFailed);
        broken.Reason.ShouldNotBeNull().ShouldContain("tarball missing");
        broken.IsBlocking.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenConflictingExactVersions_WhenLoadingWithDependencies_ThenConflictIsBlocking()
    {
        RegisterPackage("root", "1.0.0", new Dictionary<string, string> { ["A"] = "1.0.0", ["B"] = "1.0.0" });
        RegisterPackage("A", "1.0.0");
        RegisterPackage("B", "1.0.0", new Dictionary<string, string> { ["A"] = "2.0.0" });

        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        var conflict = result.ClosureMembers.Single(m => m.Status == PackageClosureMemberStatus.VersionConflict);
        conflict.PackageId.ShouldBe("A");
        conflict.Version.ShouldBe("2.0.0");
        conflict.Reason.ShouldNotBeNull().ShouldContain("1.0.0");
        conflict.IsBlocking.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenAlreadyInstalledManifestUnavailable_WhenLoadingWithDependencies_ThenManifestUnavailableIsBlocking()
    {
        RegisterPackage("root", "1.0.0", new Dictionary<string, string> { ["dep"] = "1.0.0" });
        _repository.PackageVersionExistsAsync("dep", "1.0.0", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _loader.DownloadPackageAsync("dep", "1.0.0", Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ => throw new InvalidOperationException("tarball gone"));

        var result = await _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None);

        result.ClosureMembers.ShouldContain(m =>
            m.PackageId == "dep" && m.Status == PackageClosureMemberStatus.ManifestUnavailable && m.IsBlocking);
    }

    [Fact]
    public async Task GivenAlreadyInstalledManifestRefetchCancelled_WhenLoadingWithDependencies_ThenCancellationPropagates()
    {
        RegisterPackage("root", "1.0.0", new Dictionary<string, string> { ["dep"] = "1.0.0" });
        _repository.PackageVersionExistsAsync("dep", "1.0.0", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _loader.DownloadPackageAsync("dep", "1.0.0", Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ => throw new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(
            () => _provider.LoadPackageWithDependenciesAsync("1", "root", "1.0.0", CancellationToken.None));
    }

    private void RegisterPackage(string id, string version, IReadOnlyDictionary<string, string>? deps = null)
    {
        _loader.DownloadPackageAsync(id, version, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes($"mock-{id}@{version}"))));
        _extractor.ExtractAsync(Arg.Is<Stream>(s => StreamMatches(s, id, version)), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new PackageExtractionResult
            {
                Manifest = new PackageManifest
                {
                    Name = id,
                    Version = version,
                    FhirVersion = "4.0.1",
                    Dependencies = deps ?? System.Collections.Frozen.FrozenDictionary<string, string>.Empty,
                },
                Resources = Array.Empty<ExtractedResource>(),
            }));
    }

    private static bool StreamMatches(Stream stream, string id, string version)
    {
        if (stream is not MemoryStream memory)
        {
            return false;
        }

        var content = Encoding.UTF8.GetString(memory.ToArray());
        memory.Position = 0;
        return content == $"mock-{id}@{version}";
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~ImplementationGuideProviderClosureOutcomeTests"`
Expected: build FAIL with `CS0246`/`CS1061` for `PackageClosureMember`, `PackageClosureMemberStatus`, `ClosureMembers`.

- [ ] **Step 3: Add the outcome types**

`src/Application/Ignixa.Application/Features/Packages/PackageClosureMemberStatus.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Ignixa.PackageManagement.Models;

/// <summary>
/// Outcome observed for one package while walking a dependency closure.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PackageClosureMemberStatus>))]
public enum PackageClosureMemberStatus
{
    Imported,
    AlreadyInstalled,
    ProvidedInProcess,
    VersionConflict,
    LoadFailed,
    ManifestUnavailable,
}
```

`src/Application/Ignixa.Application/Features/Packages/PackageClosureMember.cs`:

```csharp
namespace Ignixa.PackageManagement.Models;

/// <summary>
/// Typed outcome for one package observed during a dependency-closure load.
/// A blocking member means the requested closure is not fully present.
/// </summary>
public sealed record PackageClosureMember(
    string PackageId,
    string Version,
    PackageClosureMemberStatus Status,
    string? Reason = null)
{
    public bool IsBlocking => Status is PackageClosureMemberStatus.VersionConflict
        or PackageClosureMemberStatus.LoadFailed
        or PackageClosureMemberStatus.ManifestUnavailable;
}
```

Add to `PackageImportResult` after `SkippedPackages`:

```csharp
    /// <summary>
    /// Typed per-package outcomes for a dependency-closure load; empty for single-package loads.
    /// Prefer this over the legacy <see cref="LoadedPackages"/> and <see cref="SkippedPackages"/> strings.
    /// </summary>
    public IReadOnlyList<PackageClosureMember> ClosureMembers { get; init; } = [];
```

- [ ] **Step 4: Populate outcomes and stop swallowing cancellation**

In `ImplementationGuideProvider.LoadPackageWithDependenciesAsync`, declare next to `skippedSpecs`:

```csharp
        var closureMembers = new List<PackageClosureMember>();
```

Pair every legacy string with a typed entry (keep the existing strings unchanged):

```csharp
                if (existingVer == "*")
                {
                    skippedSpecs.Add($"{id}@{ver} (core package, provided in-process)");
                    closureMembers.Add(new(id, ver, PackageClosureMemberStatus.ProvidedInProcess));
                }
                else if (!string.Equals(existingVer, ver, StringComparison.OrdinalIgnoreCase))
                {
                    // existing LogWarning unchanged
                    skippedSpecs.Add($"{id}@{ver} (version conflict: @{existingVer} already queued)");
                    closureMembers.Add(new(id, ver, PackageClosureMemberStatus.VersionConflict, $"@{existingVer} already selected"));
                }
```

In the transitive `catch (Exception ex) when (!isRoot)` block, after `skippedSpecs.Add(...)`:

```csharp
                closureMembers.Add(new(id, ver, PackageClosureMemberStatus.LoadFailed, $"{ex.GetType().Name}: {ex.Message}"));
```

Change the manifest re-fetch catch so cancellation propagates, and record the outcome:

```csharp
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // existing LogWarning and skippedSpecs.Add unchanged
                    closureMembers.Add(new(id, ver, PackageClosureMemberStatus.ManifestUnavailable, $"{ex.GetType().Name}: {ex.Message}"));
                }
```

In the dependency loop, pair the two `skippedSpecs.Add` calls the same way:

```csharp
                        if (alreadyQueuedVer == "*")
                        {
                            skippedSpecs.Add($"{dep.Key}@{dep.Value} (core package, provided in-process)");
                            closureMembers.Add(new(dep.Key, dep.Value, PackageClosureMemberStatus.ProvidedInProcess));
                        }
                        else if (!string.Equals(alreadyQueuedVer, dep.Value, StringComparison.OrdinalIgnoreCase))
                        {
                            // existing LogWarning unchanged
                            skippedSpecs.Add($"{dep.Key}@{dep.Value} (version conflict: @{alreadyQueuedVer} already queued)");
                            closureMembers.Add(new(dep.Key, dep.Value, PackageClosureMemberStatus.VersionConflict, $"@{alreadyQueuedVer} already selected"));
                        }
```

After `loadedSpecs.Add(...)`:

```csharp
            closureMembers.Add(new(id, ver, alreadyLoaded ? PackageClosureMemberStatus.AlreadyInstalled : PackageClosureMemberStatus.Imported));
```

Add `ClosureMembers = closureMembers,` to the returned `PackageImportResult`.

- [ ] **Step 5: Run the new and existing provider tests**

Run: `dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~ImplementationGuideProvider"`
Expected: PASS, including the unchanged `ImplementationGuideProviderTransitiveTests` and `ImplementationGuideProviderEdgeCaseTests` (legacy strings preserved).

- [ ] **Step 6: Prove the cancellation test is a regression**

Create a temporary worktree at the base commit, copy only the cancellation test there (as a standalone class that does not reference `ClosureMembers`), run it, and confirm it FAILS (the base `catch (Exception ex)` swallows `OperationCanceledException`). Remove the worktree.

```powershell
git worktree add ..\lifecycle-base 287c54c6
# copy the single cancellation test into ..\lifecycle-base\test\Ignixa.Application.Tests\Features\Packages\CancellationRegressionProbe.cs
dotnet test ..\lifecycle-base\test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~CancellationRegressionProbe"
git worktree remove ..\lifecycle-base --force
```
Expected at base: FAIL ("Should throw OperationCanceledException but did not").

- [ ] **Step 7: Mutation-check the blocking guard**

Temporarily add `or PackageClosureMemberStatus.ProvidedInProcess` to `IsBlocking`, run the Step 5 command, confirm `GivenCoreDependency_..._ThenProvidedInProcessIsNotBlocking` and `GivenImportedAndAlreadyInstalledMembers_...` FAIL, then revert.

- [ ] **Step 8: Commit (after approval)**

```powershell
git add src/Application/Ignixa.Application/Features/Packages test/Ignixa.Application.Tests/Features/Packages/ImplementationGuideProviderClosureOutcomeTests.cs
git commit -m "Report typed package closure outcomes"
```

---

### Task 2: Activation seam and handler outcome propagation

**Files:**
- Create: `src/Application/Ignixa.Application/Features/Conformance/IPackageActivationPipeline.cs`
- Modify: `src/Application/Ignixa.Application/Features/Conformance/PackageActivationPipeline.cs:23`
- Modify: `src/Application/Ignixa.Api/Registrations/ConformanceServicesRegistration.cs:71-74`
- Modify: `src/Application/Ignixa.Application/Features/Admin/LoadPackageCommand.cs`
- Modify: `src/Application/Ignixa.Application/Features/Admin/LoadPackageHandler.cs`
- Test: `test/Ignixa.Application.Tests/Features/Admin/LoadPackageHandlerTests.cs`

**Interfaces:**
- Consumes: `PackageImportResult.ClosureMembers`, `PackageClosureMember.IsBlocking` (Task 1); existing `ActivationResult`, `ValidationIssue`.
- Produces:
  - `interface IPackageActivationPipeline { Task<ActivationResult> ActivateAsync(string packageId, string version, CancellationToken cancellationToken); }` in `Ignixa.Application.Features.Conformance`.
  - `LoadPackageResult.ClosureMembers : IReadOnlyList<PackageClosureMember>` (default `[]`).
  - `LoadPackageResult.Activation : ActivationResult?`.
  - `LoadPackageResult.IsComplete : bool` = no blocking closure member AND `Activation is { Success: true }`.

- [ ] **Step 1: Write the failing tests**

Create `test/Ignixa.Application.Tests/Features/Admin/LoadPackageHandlerTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Admin;
using Ignixa.Application.Features.Conformance;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.PackageManagement.Models;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.Admin;

public sealed class LoadPackageHandlerTests
{
    private readonly IImplementationGuideProvider _provider = Substitute.For<IImplementationGuideProvider>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IPackageActivationPipeline _activation = Substitute.For<IPackageActivationPipeline>();
    private readonly LoadPackageHandler _handler;

    public LoadPackageHandlerTests()
    {
        _handler = new LoadPackageHandler(_provider, _mediator, _activation, NullLogger<LoadPackageHandler>.Instance);
    }

    [Fact]
    public async Task GivenActivationRejectsPackage_WhenLoading_ThenActivationFailureIsReportedAndResultIsIncomplete()
    {
        _provider.LoadPackageAsync("1", "example.ig", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(new PackageImportResult { PackageId = "example.ig", PackageVersion = "1.0.0", ImportedResources = 2 });
        _activation.ActivateAsync("example.ig", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(ActivationResult.Failed([new ValidationIssue("conflict", "SearchParameter clash")]));

        var result = await _handler.HandleAsync(new LoadPackageCommand("1", "example.ig", "1.0.0"), CancellationToken.None);

        result.Activation.ShouldNotBeNull().Success.ShouldBeFalse();
        result.Activation!.Issues.ShouldHaveSingleItem().Code.ShouldBe("conflict");
        result.IsComplete.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenClosureWithBlockingMember_WhenLoadingWithDependencies_ThenMembersArePropagatedAndResultIsIncomplete()
    {
        PackageClosureMember failed = new("broken", "2.0.0", PackageClosureMemberStatus.LoadFailed, "tarball missing");
        _provider.LoadPackageWithDependenciesAsync("1", "example.ig", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(new PackageImportResult
            {
                PackageId = "example.ig",
                PackageVersion = "1.0.0",
                ClosureMembers = [new("example.ig", "1.0.0", PackageClosureMemberStatus.Imported), failed],
            });
        _activation.ActivateAsync("example.ig", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(ActivationResult.Succeeded());

        var result = await _handler.HandleAsync(
            new LoadPackageCommand("1", "example.ig", "1.0.0", IncludeDependencies: true), CancellationToken.None);

        result.ClosureMembers.ShouldContain(failed);
        result.IsComplete.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenOnlyInformationalMembersAndActivationSucceeds_WhenLoadingWithDependencies_ThenResultIsComplete()
    {
        _provider.LoadPackageWithDependenciesAsync("1", "example.ig", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(new PackageImportResult
            {
                PackageId = "example.ig",
                PackageVersion = "1.0.0",
                ClosureMembers =
                [
                    new("example.ig", "1.0.0", PackageClosureMemberStatus.Imported),
                    new("hl7.fhir.r4.core", "4.0.1", PackageClosureMemberStatus.ProvidedInProcess),
                ],
            });
        _activation.ActivateAsync("example.ig", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(ActivationResult.Succeeded(["Patient"]));

        var result = await _handler.HandleAsync(
            new LoadPackageCommand("1", "example.ig", "1.0.0", IncludeDependencies: true), CancellationToken.None);

        result.IsComplete.ShouldBeTrue();
        result.Activation.ShouldNotBeNull().PendingReindex.ShouldBe(["Patient"]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~LoadPackageHandlerTests"`
Expected: build FAIL (`IPackageActivationPipeline`, `LoadPackageResult.Activation`, `IsComplete`, `ClosureMembers` missing).

- [ ] **Step 3: Add the activation seam**

`src/Application/Ignixa.Application/Features/Conformance/IPackageActivationPipeline.cs`:

```csharp
namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Activates an installed package through the event-sourced conformance pipeline.
/// </summary>
public interface IPackageActivationPipeline
{
    Task<ActivationResult> ActivateAsync(string packageId, string version, CancellationToken cancellationToken);
}
```

Change `public class PackageActivationPipeline(` declaration to end with `) : IPackageActivationPipeline`. In `ConformanceServicesRegistration`:

```csharp
        builder.RegisterType<PackageActivationPipeline>()
            .AsSelf()
            .As<IPackageActivationPipeline>()
            .InstancePerDependency();
```

(`AsSelf` stays because E2E tests resolve the concrete type.)

- [ ] **Step 4: Extend `LoadPackageResult`**

Add `using Ignixa.Application.Features.Conformance;` and `using Ignixa.PackageManagement.Models;` to `LoadPackageCommand.cs`, then append to `LoadPackageResult`:

```csharp
    /// <summary>
    /// Typed per-package closure outcomes; empty for single-package loads.
    /// </summary>
    public IReadOnlyList<PackageClosureMember> ClosureMembers { get; init; } = [];

    /// <summary>
    /// Activation outcome for the requested root package.
    /// </summary>
    public ActivationResult? Activation { get; init; }

    /// <summary>
    /// True only when no closure member is blocking and root activation succeeded.
    /// Installed content is retained when this is false.
    /// </summary>
    public bool IsComplete => ClosureMembers.All(m => !m.IsBlocking) && Activation is { Success: true };
```

- [ ] **Step 5: Populate the result in the handler**

In `LoadPackageHandler`, change the constructor parameter and field type from `PackageActivationPipeline` to `IPackageActivationPipeline`. Move the `ActivateAsync` call before building the result, and build the result with the new fields:

```csharp
            var activationResult = await _activationPipeline.ActivateAsync(
                request.PackageId,
                request.Version,
                cancellationToken);

            var result = new LoadPackageResult
            {
                PackageId = importResult.PackageId,
                PackageVersion = importResult.PackageVersion,
                TotalResources = importResult.TotalResources,
                ImportedResources = importResult.ImportedResources,
                DurationMilliseconds = (long)importResult.Duration.TotalMilliseconds,
                ResourcesByType = importResult.ResourcesByType,
                LoadedPackages = importResult.LoadedPackages,
                SkippedPackages = importResult.SkippedPackages,
                ClosureMembers = importResult.ClosureMembers,
                Activation = activationResult,
            };
```

Keep the existing activation and skipped-dependency log statements, now after result construction. Change the success log to report completeness:

```csharp
            _logger.LogInformation(
                "Package {PackageId}@{Version} load finished. Resources: {Count}, Duration: {Duration}ms, Complete: {IsComplete}",
                result.PackageId, result.PackageVersion, result.ImportedResources, result.DurationMilliseconds, result.IsComplete);
```

- [ ] **Step 6: Fix the MCP tool's constructor type so the solution compiles**

In `InstallPackageTool.cs`, change `PackageActivationPipeline activationPipeline` and its field to `IPackageActivationPipeline`. (Behavior change comes in Task 4.)

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~LoadPackageHandlerTests|FullyQualifiedName~ImplementationGuideProvider"`
Expected: PASS.

- [ ] **Step 8: Mutation-check `IsComplete`**

Temporarily change `IsComplete` to ignore `Activation` (`=> ClosureMembers.All(m => !m.IsBlocking)`), confirm `GivenActivationRejectsPackage_...` FAILS, revert.

- [ ] **Step 9: Commit (after approval)**

```powershell
git add src/Application/Ignixa.Application/Features/Conformance src/Application/Ignixa.Application/Features/Admin src/Application/Ignixa.Api/Registrations/ConformanceServicesRegistration.cs src/Application/Ignixa.Application/Features/Experimental/Mcp/Tools/PackageManagement/InstallPackageTool.cs test/Ignixa.Application.Tests/Features/Admin
git commit -m "Carry closure and activation outcomes in load results"
```

---

### Task 3: HTTP load response and cancellation

**Files:**
- Modify: `src/Application/Ignixa.Api/Endpoints/AdminPackageEndpoints.cs` (`HandleLoadPackage`, `LoadPackageResponse`, new nested `PackageActivationResponse` beside the existing nested DTOs)
- Test: `test/Ignixa.Api.Tests/Endpoints/AdminPackageEndpointLoadOutcomeTests.cs`

**Interfaces:**
- Consumes: `LoadPackageResult.ClosureMembers`, `.Activation`, `.IsComplete`, `.LoadedPackages`, `.SkippedPackages` (Task 2).
- Produces JSON (camelCase, additive): `isComplete`, `loadedPackages`, `skippedPackages`, `closureMembers[] { packageId, version, status (string), reason, isBlocking }`, `activation { succeeded, issues[] { code, message, resourceType, parameterCode }, pendingReindex[] }`. HTTP status remains 200 for completed handler calls.

- [ ] **Step 1: Write the failing tests**

Create `test/Ignixa.Api.Tests/Endpoints/AdminPackageEndpointLoadOutcomeTests.cs`:

```csharp
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Api.Endpoints;
using Ignixa.Api.Middleware;
using Ignixa.Application.Features.Admin;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.PackageManagement.Models;
using Medino;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Endpoints;

public sealed class AdminPackageEndpointLoadOutcomeTests : IAsyncLifetime
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly WebApplication _app;

    public AdminPackageEndpointLoadOutcomeTests()
    {
        TenantConfiguration tenant = new() { TenantId = 1, DisplayName = "One", FhirVersion = "4.0" };
        var store = Substitute.For<ITenantConfigurationStore>();
        store.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration?>(tenant));
        store.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<TenantConfiguration>>([tenant]));
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(_mediator);
        builder.Services.AddSingleton(store);
        _app = builder.Build();
    }

    [Fact]
    public async Task GivenIncompleteClosureAndRejectedActivation_WhenLoadingPackage_ThenResponseReportsStructuredOutcome()
    {
        _mediator.SendAsync(Arg.Any<LoadPackageCommand>(), Arg.Any<CancellationToken>()).Returns(new LoadPackageResult
        {
            PackageId = "example.ig",
            PackageVersion = "1.2.3",
            LoadedPackages = ["example.ig@1.2.3"],
            SkippedPackages = ["broken@2.0.0 (skipped: InvalidOperationException: tarball missing)"],
            ClosureMembers =
            [
                new("example.ig", "1.2.3", PackageClosureMemberStatus.Imported),
                new("broken", "2.0.0", PackageClosureMemberStatus.LoadFailed, "tarball missing"),
            ],
            Activation = ActivationResult.Failed([new ValidationIssue("conflict", "SearchParameter clash")]),
        });

        var (context, body) = await SendLoadAsync(CancellationToken.None);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        body["isComplete"]!.GetValue<bool>().ShouldBeFalse();
        body["skippedPackages"]![0]!.GetValue<string>().ShouldStartWith("broken@2.0.0");
        body["closureMembers"]![1]!["status"]!.GetValue<string>().ShouldBe("LoadFailed");
        body["closureMembers"]![1]!["isBlocking"]!.GetValue<bool>().ShouldBeTrue();
        body["activation"]!["succeeded"]!.GetValue<bool>().ShouldBeFalse();
        body["activation"]!["issues"]![0]!["code"]!.GetValue<string>().ShouldBe("conflict");
    }

    [Fact]
    public async Task GivenCallerCancels_WhenLoadingPackage_ThenCancellationIsNotConvertedToServerError()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        _mediator.SendAsync(Arg.Any<LoadPackageCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        await Should.ThrowAsync<OperationCanceledException>(() => SendLoadAsync(cancellation.Token));
    }

    private async Task<(HttpContext Context, JsonNode Body)> SendLoadAsync(CancellationToken requestAborted)
    {
        var pipeline = new ApplicationBuilder(_app.Services);
        pipeline.UseRouting();
        pipeline.UseMiddleware<TenantResolutionMiddleware>();
        pipeline.UseEndpoints(endpoints => endpoints.MapAdminPackageEndpoints());
        await using var scope = _app.Services.CreateAsyncScope();
        byte[] payload = Encoding.UTF8.GetBytes("""{"packageId":"example.ig","version":"1.2.3","includeDependencies":true}""");
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = requestAborted };
        context.Request.Method = "POST";
        context.Request.Path = "/tenant/1/admin/packages/load";
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.Body = new MemoryStream(payload);
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = payload.Length;
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyDetectionFeature());
        context.Response.Body = new MemoryStream();
        await pipeline.Build()(context);
        context.Response.Body.Position = 0;
        return (context, (await JsonNode.ParseAsync(context.Response.Body))!);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private sealed class RequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test\Ignixa.Api.Tests\Ignixa.Api.Tests.csproj --filter "FullyQualifiedName~AdminPackageEndpointLoadOutcomeTests"`
Expected: first test FAIL (`isComplete` node null); second FAIL (endpoint returns 500 instead of throwing).

- [ ] **Step 3: Extend the response DTO**

Add `using Ignixa.Application.Features.Conformance;` and `using Ignixa.PackageManagement.Models;`. Append to `LoadPackageResponse`:

```csharp
        /// <summary>
        /// True only when the dependency closure has no blocking member and root activation succeeded.
        /// </summary>
        public bool IsComplete { get; init; }

        /// <summary>
        /// Legacy closure description strings; prefer <see cref="ClosureMembers"/>.
        /// </summary>
        public IReadOnlyList<string>? LoadedPackages { get; init; }

        /// <summary>
        /// Legacy skipped-package description strings; prefer <see cref="ClosureMembers"/>.
        /// </summary>
        public IReadOnlyList<string>? SkippedPackages { get; init; }

        /// <summary>
        /// Typed per-package closure outcomes.
        /// </summary>
        public IReadOnlyList<PackageClosureMember> ClosureMembers { get; init; } = [];

        /// <summary>
        /// Root package activation outcome.
        /// </summary>
        public PackageActivationResponse? Activation { get; init; }
```

Add a nested record beside the other DTOs:

```csharp
    /// <summary>
    /// Activation outcome for the requested root package.
    /// </summary>
    public record PackageActivationResponse(
        bool Succeeded,
        IReadOnlyList<ValidationIssue> Issues,
        IReadOnlyList<string> PendingReindex);
```

- [ ] **Step 4: Map outcomes and propagate cancellation**

In `HandleLoadPackage`, extend the response mapping:

```csharp
            var response = new LoadPackageResponse
            {
                PackageId = result.PackageId,
                PackageVersion = result.PackageVersion,
                TotalResources = result.TotalResources,
                ImportedResources = result.ImportedResources,
                DurationMilliseconds = result.DurationMilliseconds,
                ResourcesByType = result.ResourcesByType,
                IsComplete = result.IsComplete,
                LoadedPackages = result.LoadedPackages,
                SkippedPackages = result.SkippedPackages,
                ClosureMembers = result.ClosureMembers,
                Activation = result.Activation is { } activation
                    ? new PackageActivationResponse(activation.Success, activation.Issues, activation.PendingReindex)
                    : null,
            };
```

Change the success log message to `"Package {PackageId}@{Version} load finished. {Count} resources imported. Complete: {IsComplete}"` with `result.IsComplete`. Insert before `catch (ArgumentException ex)`:

```csharp
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
```

- [ ] **Step 5: Run endpoint tests**

Run: `dotnet test test\Ignixa.Api.Tests\Ignixa.Api.Tests.csproj --filter "FullyQualifiedName~AdminPackageEndpoint"`
Expected: PASS for both new tests and the unchanged `AdminPackageEndpointRoutingTests`.

- [ ] **Step 6: Prove the cancellation test is a regression**

Use the base-commit worktree procedure from Task 1 Step 6 with a probe containing only `GivenCallerCancels_...` (it compiles at base because it uses only existing types). Expected at base: FAIL (500 response, no exception).

- [ ] **Step 7: Commit (after approval)**

```powershell
git add src/Application/Ignixa.Api/Endpoints/AdminPackageEndpoints.cs test/Ignixa.Api.Tests/Endpoints/AdminPackageEndpointLoadOutcomeTests.cs
git commit -m "Expose package load outcomes over HTTP"
```

---

### Task 4: MCP install reports activation failure

**Files:**
- Modify: `src/Application/Ignixa.Application/Features/Experimental/Mcp/Dtos/InstallPackageResultDto.cs`
- Modify: `src/Application/Ignixa.Application/Features/Experimental/Mcp/Tools/PackageManagement/InstallPackageTool.cs:85-135`
- Test: `test/Ignixa.Api.Tests/Mcp/InstallPackageToolTests.cs`

**Interfaces:**
- Consumes: `IPackageActivationPipeline` (Task 2).
- Produces: `InstallPackageResultDto.ActivationIssues : IReadOnlyList<string>` and `.PendingReindex : IReadOnlyList<string>` (default `[]`); `Success` now equals activation success.

- [ ] **Step 1: Write the failing tests**

Create `test/Ignixa.Api.Tests/Mcp/InstallPackageToolTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Experimental.Mcp.Tools.PackageManagement;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Mcp;

public sealed class InstallPackageToolTests : McpTestBase
{
    private readonly IImplementationGuideProvider _provider = Substitute.For<IImplementationGuideProvider>();
    private readonly IPackageActivationPipeline _activation = Substitute.For<IPackageActivationPipeline>();
    private readonly InstallPackageTool _tool;

    public InstallPackageToolTests()
    {
        SetupTenant(1, "One");
        _provider.LoadPackageAsync("1", "example.ig", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(new PackageImportResult { PackageId = "example.ig", PackageVersion = "1.0.0", ImportedResources = 2 });
        _tool = new InstallPackageTool(
            HttpContextAccessor,
            TenantConfigurationStore,
            _provider,
            Substitute.For<INpmPackageSearchService>(),
            _activation,
            NullLogger<InstallPackageTool>.Instance);
    }

    [Fact]
    public async Task GivenActivationRejectsPackage_WhenInstalling_ThenResultIsUnsuccessfulAndListsIssues()
    {
        _activation.ActivateAsync("example.ig", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(ActivationResult.Failed([new ValidationIssue("conflict", "SearchParameter clash")]));

        var result = await _tool.InstallPackageAsync("example.ig", "1.0.0", 1, CancellationToken.None);

        result.Success.ShouldBeFalse();
        result.ActivationIssues.ShouldHaveSingleItem().ShouldContain("SearchParameter clash");
        result.Message.ShouldContain("activation failed");
    }

    [Fact]
    public async Task GivenActivationSucceedsWithPendingReindex_WhenInstalling_ThenResultIsSuccessfulAndReportsReindex()
    {
        _activation.ActivateAsync("example.ig", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(ActivationResult.Succeeded(["Patient"]));

        var result = await _tool.InstallPackageAsync("example.ig", "1.0.0", 1, CancellationToken.None);

        result.Success.ShouldBeTrue();
        result.PendingReindex.ShouldBe(["Patient"]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test\Ignixa.Api.Tests\Ignixa.Api.Tests.csproj --filter "FullyQualifiedName~InstallPackageToolTests"`
Expected: build FAIL (`ActivationIssues`, `PendingReindex` missing).

- [ ] **Step 3: Extend the DTO**

Append to `InstallPackageResultDto`:

```csharp
    /// <summary>
    /// Activation validation issues; empty when activation succeeded.
    /// </summary>
    public IReadOnlyList<string> ActivationIssues { get; init; } = [];

    /// <summary>
    /// Resource types that require reindexing after activation.
    /// </summary>
    public IReadOnlyList<string> PendingReindex { get; init; } = [];
```

- [ ] **Step 4: Report the real outcome**

In `InstallPackageTool.InstallPackageAsync`, replace the returned DTO with:

```csharp
        var activationIssues = activationResult.Issues.Select(i => $"{i.Code}: {i.Message}").ToList();
        var message = activationResult.Success
            ? $"Successfully installed {result.PackageId}@{result.PackageVersion} " +
              $"({result.ImportedResources} new, {result.UpdatedResources} updated)"
            : $"Installed {result.PackageId}@{result.PackageVersion} but activation failed: " +
              string.Join("; ", activationIssues);

        return new InstallPackageResultDto
        {
            Success = activationResult.Success,
            TenantId = resolvedTenantId,
            TenantName = tenantConfig.DisplayName,
            PackageId = result.PackageId,
            PackageVersion = result.PackageVersion,
            TotalResources = result.TotalResources,
            ImportedResources = result.ImportedResources,
            UpdatedResources = result.UpdatedResources,
            DurationSeconds = (int)duration.TotalSeconds,
            ResourcesByType = resourcesByType,
            ActivationIssues = activationIssues,
            PendingReindex = activationResult.PendingReindex,
            Message = message,
        };
```

Update the tool `[Description]` line `Returns statistics about imported resources (counts, types, duration).` to `Returns statistics plus activation outcome; Success is false when activation is rejected (resources stay installed but inactive).`

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test test\Ignixa.Api.Tests\Ignixa.Api.Tests.csproj --filter "FullyQualifiedName~InstallPackageToolTests|FullyQualifiedName~TenantAwareMcpToolTests"`
Expected: PASS.

- [ ] **Step 6: Commit (after approval)**

```powershell
git add src/Application/Ignixa.Application/Features/Experimental/Mcp test/Ignixa.Api.Tests/Mcp/InstallPackageToolTests.cs
git commit -m "Report MCP package activation failures"
```

---

### Task 5: Preload warnings, published docs, and full validation

**Files:**
- Modify: `src/Application/Ignixa.Api/Services/TenantPackagePreloadService.cs:200-215`
- Modify: `docs/site/docs/server/fhir/operations.md:436-441`
- Modify: `docs/site/docs/server/features/mcp-server.md:190-215`

**Interfaces:**
- Consumes: `LoadPackageResult.IsComplete`, `.Activation`, `.ClosureMembers` (Task 2).

The preload change is a logging-only branch on `IsComplete`, whose semantics are already pinned by Task 2 tests. A unit test of `TenantPackagePreloadService` would need the full hosted-service container, so this task is verified by build plus the existing suites.

- [ ] **Step 1: Replace the unconditional success log in preload**

Replace the `"Successfully loaded {PackageId}@{Version} ..."` `LogInformation` call with:

```csharp
                            if (result.IsComplete)
                            {
                                _logger.LogInformation(
                                    "Successfully loaded {PackageId}@{Version} for tenant {TenantId}. Imported {Count} resources",
                                    packageId,
                                    version,
                                    tenant.TenantId,
                                    result.ImportedResources);
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "Loaded {PackageId}@{Version} for tenant {TenantId} incompletely. Activation succeeded: {ActivationSucceeded}. Activation issues: {ActivationIssues}. Blocking closure members: {BlockingMembers}",
                                    packageId,
                                    version,
                                    tenant.TenantId,
                                    result.Activation?.Success,
                                    string.Join("; ", result.Activation?.Issues.Select(i => $"{i.Code}: {i.Message}") ?? []),
                                    string.Join("; ", result.ClosureMembers.Where(m => m.IsBlocking).Select(m => $"{m.PackageId}@{m.Version} {m.Status}")));
                            }
```

- [ ] **Step 2: Document the HTTP response**

After the route list in `operations.md` (line 440), add:

```markdown
`POST .../admin/packages/load` accepts `packageId`, `version`, and optional `includeDependencies`.
A `200 OK` response means the request ran, not that the package is usable. Check `isComplete`:
it is `true` only when no dependency in `closureMembers` is blocking (`VersionConflict`,
`LoadFailed`, or `ManifestUnavailable`) and `activation.succeeded` is `true`. `ProvidedInProcess`
entries (core FHIR packages) are informational. When activation is rejected, imported resources
remain installed but inactive, and `activation.issues` explains why; `activation.pendingReindex`
lists resource types to reindex after a successful activation. `loadedPackages` and
`skippedPackages` are retained for compatibility.
```

- [ ] **Step 3: Document the MCP result**

After the `install_fhir_package` example in `mcp-server.md`, add:

```markdown
`success` is `false` when the package was imported but activation was rejected; inspect
`activationIssues`. `pendingReindex` lists resource types to reindex after activation.
```

- [ ] **Step 4: Build the solution**

Run: `dotnet build All.sln`
Expected: `Build succeeded.` with 0 warnings (warnings are errors).

- [ ] **Step 5: Run the affected suites**

Run: `dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Packages|FullyQualifiedName~Admin|FullyQualifiedName~Conformance"`
Run: `dotnet test test\Ignixa.Api.Tests\Ignixa.Api.Tests.csproj`
Expected: 0 failed in both. If any `Ignixa.Api.E2ETests` require SQL, they are out of this plan's local gate; note them in the PR as not run.

- [ ] **Step 6: Commit (after approval)**

```powershell
git add src/Application/Ignixa.Api/Services/TenantPackagePreloadService.cs docs/site/docs/server/fhir/operations.md docs/site/docs/server/features/mcp-server.md
git commit -m "Warn on incomplete package preload and document outcomes"
```

---

## Roadmap: follow-on plans (not executable from this document)

Each slice below needs its own plan because it requires schema or contract decisions this plan cannot pre-empt.

1. **Slice 2 - Durable installation completion.** Decide the installation-record schema (identity, source ID, digest, manifest, edges, generation, `Installing`/`Installed`/`Failed`, owner lease) for SQL and file-system providers; a SQL migration; generation-gated visibility in `SqlServerPackageResourceRepository` and `ConformanceResourceResolver`; replacing `PackageVersionExistsAsync` with an installation-state query; cross-source same-identity coordination; unload/uninstall through the same boundary; restart recovery; and classification of pre-existing rows. Acceptance: failure after batch 1 of a >500-resource package, stale-owner completion, concurrent same-identity installs, changed bytes for an immutable identity.
2. **Slice 3 - Dependency planning.** A pure planner (exact versions only) that rejects missing, conflicting, cyclic, and FHIR-incompatible closures before any write; persisted edges; dependency-first activation that blocks dependants on prerequisite failure; explicit legacy best-effort mode returning the Task 1 outcomes.
3. **Slice 4 - Verified acquisition integration.** Wire `NpmPackageAcquirer`/`VerifiedPackageCache` and approved source policies into server registration (`PackageManagementRegistration.cs`) and the validation CLI, replacing the legacy loader and permissive extractor; CLI reports closure outcomes.
4. **Slice 5 - Version semantics and qualification.** Replace the SQL `SemanticVersionComparer` suffix ordering with SemVer 2.0 precedence (preserving build-metadata identity), pinned multi-release IG fixtures, and lifecycle qualification tests; update ADR-2610 status when all slices ship.

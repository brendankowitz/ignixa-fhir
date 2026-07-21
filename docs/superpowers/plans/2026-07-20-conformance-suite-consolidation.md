# Conformance Suite Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `ignixa-fhir` the canonical home of the 87-script TestScript conformance corpus at `src/Core/Ignixa.TestScript.Suites/testscripts/`, published as the `Ignixa.TestScript.Suites` NuGet package.

**Architecture:** A content-only netstandard2.0 project packs the suite JSON plus a `build/*.targets` file that copies suites into any consumer's output under `testscripts/`. In-repo test projects `<Import>` that same targets file explicitly, so both in-repo and packaged consumers resolve suites identically at `AppContext.BaseDirectory/testscripts/`. Repo-root `conformance-tests/` is deleted.

**Tech Stack:** .NET 10 SDK, MSBuild/NuGet pack, xUnit + Shouldly, `Ignixa.TestScript` parser.

**Source spec:** `docs/superpowers/specs/2026-07-20-conformance-suite-consolidation-design.md`

## Global Constraints

- This plan covers **Phase 1 only** — everything lands in `ignixa-fhir`. `ignixa-lab` keeps its local `IgnixaLab.TestScript.Suites` package and is not modified except for the freeze note in Task 8. Do not delete or repoint anything in lab.
- Source of truth for the suite content is `E:\data\src\ignixa-lab\backend\src\Ignixa.Lab.Suites\` (a local clone). Copy from there; never from `ignixa-fhir/conformance-tests/`, which is the stale copy.
- The folder name `testscripts/` and the pack `PackagePath="testscripts/"` are **frozen**. Lab's `Suites/SuiteCatalog.cs` reads that exact layout. Do not rename either.
- Every `src/Core/**` project must declare `<PackageStability>` explicitly or `PackageStabilityGuardTests` fails (ADR 2606). Use `alpha`.
- Every packable project must contain a `README.md` or pack fails: root `Directory.Build.props:10` sets `PackageReadmeFile=README.md` unconditionally, but line 65 only packs it `Condition="Exists('README.md')"`.
- `TreatWarningsAsErrors=true` is set repo-wide in `Directory.Build.props:14`.
- Do **not** copy `bin/` or `obj/` from lab's project directory.
- Do **not** convert the explicit `<Import>` wiring in Tasks 4 and 5 into a `ProjectReference`. `build/*.targets` auto-import applies to `PackageReference` only; a `ProjectReference` silently stops copying the suites.
- Commit after every task. Do not squash tasks together.

---

### Task 1: Create the Ignixa.TestScript.Suites project

**Files:**
- Create: `src/Core/Ignixa.TestScript.Suites/Ignixa.TestScript.Suites.csproj`
- Create: `src/Core/Ignixa.TestScript.Suites/README.md`
- Create: `src/Core/Ignixa.TestScript.Suites/build/Ignixa.TestScript.Suites.targets`
- Create: `src/Core/Ignixa.TestScript.Suites/testscripts/**/*.json` (87 files, copied)
- Modify: `All.sln`

**Interfaces:**
- Consumes: nothing.
- Produces: package id `Ignixa.TestScript.Suites`; a targets file at `src/Core/Ignixa.TestScript.Suites/build/Ignixa.TestScript.Suites.targets` that any project may `<Import>` to get suites copied to `$(OutDir)testscripts/`.

- [ ] **Step 1: Copy the suite tree from lab**

```bash
cd /e/data/src/ignixa-fhir
mkdir -p src/Core/Ignixa.TestScript.Suites
cp -r /e/data/src/ignixa-lab/backend/src/Ignixa.Lab.Suites/testscripts src/Core/Ignixa.TestScript.Suites/
cp -r /e/data/src/ignixa-lab/backend/src/Ignixa.Lab.Suites/build src/Core/Ignixa.TestScript.Suites/
```

- [ ] **Step 2: Verify the copy landed and excluded build output**

```bash
cd /e/data/src/ignixa-fhir/src/Core/Ignixa.TestScript.Suites
find testscripts -name '*.json' | wc -l
ls testscripts
find . -type d -name bin -o -type d -name obj
```

Expected: `87`; the nine category folders `Bundles CRUD Foundation Microsoft Operations Regression Search Subscriptions Validation`; and **no output** from the third command.

- [ ] **Step 3: Write the project file**

Create `src/Core/Ignixa.TestScript.Suites/Ignixa.TestScript.Suites.csproj`. This is lab's csproj with `PackageId` renamed, the hardcoded `<Version>` dropped in favour of repo versioning, `<PackageStability>` added for ADR 2606, and the description rewritten. The comments are carried over deliberately — they record MSBuild behaviour that is expensive to rediscover.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <IsPackable>true</IsPackable>
    <PackageId>Ignixa.TestScript.Suites</PackageId>
    <PackageStability>alpha</PackageStability>
    <Description>
      Canonical FHIR TestScript conformance suites for the Ignixa TestScript engine,
      packaged as NuGet content. Consumers receive the suites in their build output
      under testscripts/, preserving the category subfolders.
    </Description>
    <!-- Content-only package: nothing to compile, so skip the empty-lib warning. -->
    <NoWarn>$(NoWarn);NU5128</NoWarn>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <ContinuousIntegrationBuild Condition="'$(GITHUB_ACTIONS)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>

  <ItemGroup>
    <None Include="testscripts/**/*.json" Pack="true" PackagePath="testscripts/" />
    <None Include="build/Ignixa.TestScript.Suites.targets" Pack="true" PackagePath="build/" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.SourceLink.GitHub" PrivateAssets="All" />
  </ItemGroup>

  <!--
    Stamps the packed testscripts with the exact commit they came from, so consumers can
    link back to a permalink blob instead of a moving `main` ref. Written to obj/ (not
    source-controlled) and packed alongside the JSON fixtures via NuGet's documented
    TfmSpecificPackageFile extension point (see build/Ignixa.TestScript.Suites.targets for
    how consumers receive it) — a plain BeforeTargets="Pack"/"GenerateNuspec" hook is
    unreliable here since MSBuild can skip those targets outright via their own
    up-to-date checks.
  -->
  <PropertyGroup>
    <TargetsForTfmSpecificContentInPackage>$(TargetsForTfmSpecificContentInPackage);WriteSourceRevisionFile</TargetsForTfmSpecificContentInPackage>
  </PropertyGroup>

  <Target Name="WriteSourceRevisionFile" DependsOnTargets="InitializeSourceControlInformation">
    <PropertyGroup>
      <!--
        TfmSpecificPackageFile's PackagePath is a target *directory* that preserves the
        source file's own name (PackagePath="testscripts/" below, not
        "testscripts/source-revision.txt" — that would nest it under a same-named
        subdirectory instead of naming the file). A dotfile name isn't an option here:
        NuGet's default pack excludes anything starting with '.'.
      -->
      <SourceRevisionFilePath>$(IntermediateOutputPath)source-revision.txt</SourceRevisionFilePath>
    </PropertyGroup>
    <WriteLinesToFile File="$(SourceRevisionFilePath)" Lines="$(SourceRevisionId)" Overwrite="true" />
    <ItemGroup>
      <TfmSpecificPackageFile Include="$(SourceRevisionFilePath)">
        <PackagePath>testscripts/</PackagePath>
      </TfmSpecificPackageFile>
    </ItemGroup>
  </Target>

</Project>
```

- [ ] **Step 4: Rename the targets file and update its comment**

```bash
cd /e/data/src/ignixa-fhir/src/Core/Ignixa.TestScript.Suites/build
mv IgnixaLab.TestScript.Suites.targets Ignixa.TestScript.Suites.targets
```

Then replace the file's contents with the following. The only functional change from lab's version is none — the glob and guards are identical. The comments are updated to drop lab-specific references (`Ignixa.Lab.Suites.csproj`, `HealthFunction`) and to record that this file is imported directly by in-repo projects as well as auto-imported by package consumers.

```xml
<Project>

  <!--
    Copies the canonical TestScript suites into the consumer's output under testscripts/,
    preserving the category subfolders that consumers enumerate.

    Two delivery paths, one glob: in the NuGet package this file sits at build/ with the
    suites at testscripts/ under the package root; in this repo it sits at build/ with the
    suites at testscripts/ under the project root. The relative path is identical either
    way, so `../testscripts` is correct for both.

    NuGet auto-imports this for PackageReference consumers. In-repo test projects must
    <Import> it explicitly — build/*.targets auto-import does NOT apply to
    ProjectReference. Converting those imports to a ProjectReference silently stops the
    copy and leaves the suites missing at runtime.
  -->
  <ItemGroup>
    <None Include="$(MSBuildThisFileDirectory)../testscripts/**/*.json">
      <Link>testscripts/%(RecursiveDir)%(Filename)%(Extension)</Link>
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
      <Pack>false</Pack>
      <Visible>false</Visible>
    </None>
    <!--
      Commit the packed testscripts came from (WriteSourceRevisionFile in
      Ignixa.TestScript.Suites.csproj); not a *.json fixture so the glob above doesn't
      cover it. Guarded by Exists() — unlike that glob, a literal Include with
      CopyToOutputDirectory set hard-fails the consumer's build (MSB3030) if the file is
      absent, e.g. a stale cached local-feed package predating this file's introduction,
      or any in-repo build (where it only exists after pack). Missing here just means
      consumers get no revision stamp, same as an unreadable file at runtime.
    -->
    <None Include="$(MSBuildThisFileDirectory)../testscripts/source-revision.txt" Condition="Exists('$(MSBuildThisFileDirectory)../testscripts/source-revision.txt')">
      <Link>testscripts/source-revision.txt</Link>
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
      <Pack>false</Pack>
      <Visible>false</Visible>
    </None>
  </ItemGroup>

</Project>
```

- [ ] **Step 5: Write the package README**

Create `src/Core/Ignixa.TestScript.Suites/README.md`. Required for pack to succeed (see Global Constraints).

```markdown
# Ignixa.TestScript.Suites

Canonical FHIR `TestScript` conformance suites for the [Ignixa](https://github.com/brendankowitz/ignixa-fhir)
TestScript engine, shipped as NuGet content.

Adding a `PackageReference` copies the suites into your build output under `testscripts/`,
preserving category subfolders:

```
testscripts/
  Bundles/ CRUD/ Foundation/ Microsoft/ Operations/
  Regression/ Search/ Subscriptions/ Validation/
  source-revision.txt
```

Resolve them at runtime with `Path.Combine(AppContext.BaseDirectory, "testscripts")`.

`source-revision.txt` holds the exact `ignixa-fhir` commit the suites were packed from, so
a report can link to a permalink rather than a moving `main` ref.

## Running them

```bash
dotnet tool install -g Ignixa.ConformanceMatrix.Cli
ignixa-matrix run --server https://your-fhir-server --tests ./testscripts \
  --impl my-server --out ./reports/my-server.json
```

## Extensions

Several suites use the four Ignixa TestScript extensions described in
[ADR 2607](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2607-testscript-extensions.md).
Three are ignore-safe on a plain engine; suites using `fhirfakes` require an engine that
understands it. Suites and engine are versioned together for this reason.
```

- [ ] **Step 6: Add the project to the solution**

```bash
cd /e/data/src/ignixa-fhir
dotnet sln All.sln add src/Core/Ignixa.TestScript.Suites/Ignixa.TestScript.Suites.csproj
```

- [ ] **Step 7: Verify the project builds and packs**

```bash
cd /e/data/src/ignixa-fhir
dotnet pack src/Core/Ignixa.TestScript.Suites/Ignixa.TestScript.Suites.csproj -c Release -o /tmp/suites-pack
```

Expected: build succeeds with 0 warnings and 0 errors; a `.nupkg` is produced. If it fails
with NU5039, the README from Step 5 is missing or misnamed.

- [ ] **Step 8: Verify the package layout**

```bash
cd /tmp/suites-pack
unzip -l Ignixa.TestScript.Suites.*.nupkg | grep -E 'testscripts/|build/' | head -20
unzip -l Ignixa.TestScript.Suites.*.nupkg | grep -c 'testscripts/.*\.json'
unzip -l Ignixa.TestScript.Suites.*.nupkg | grep 'source-revision.txt'
```

Expected: paths under `testscripts/<Category>/*.json` and `build/Ignixa.TestScript.Suites.targets`;
a count of `87`; and exactly one `testscripts/source-revision.txt` entry.

- [ ] **Step 9: Verify the repo guards still pass**

```bash
cd /e/data/src/ignixa-fhir
dotnet test test/Ignixa.RepoGuards.Tests/Ignixa.RepoGuards.Tests.csproj
```

Expected: PASS. `PackageStabilityGuardTests` proves the `<PackageStability>alpha</PackageStability>`
declaration is present. `RuntimeMultiTargetingGuardTests` passes because `netstandard2.0`
does not overlap `{net9.0, net10.0}` and is exempt by design.

- [ ] **Step 10: Commit**

```bash
cd /e/data/src/ignixa-fhir
git add src/Core/Ignixa.TestScript.Suites All.sln
git commit -m "feat(testscript): add Ignixa.TestScript.Suites content package

Seeds the canonical conformance corpus (87 scripts) from ignixa-lab's
Ignixa.Lab.Suites, which had grown to 6.7x the stale repo-root copy.
Packaging machinery carried over verbatim; PackageId, PackageStability
and README added for this repo's pack rules."
```

---

### Task 2: Reconcile the three divergent suites

Ten of the thirteen files that exist in both trees are byte-identical and none exist only in `ignixa-fhir`. Three differ in both directions, so lab's copy is not a strict superset and a blind overwrite would silently drop assertions.

**Files:**
- Modify: `src/Core/Ignixa.TestScript.Suites/testscripts/Bundles/transaction.json`
- Modify: `src/Core/Ignixa.TestScript.Suites/testscripts/Search/chaining.json`
- Modify: `src/Core/Ignixa.TestScript.Suites/testscripts/Validation/validate-op.json`

**Interfaces:**
- Consumes: the suite tree created in Task 1.
- Produces: a reconciled corpus; no API surface.

- [ ] **Step 1: Confirm the divergence set has not changed**

```bash
cd /e/data/src/ignixa-fhir/conformance-tests
NEW=../src/Core/Ignixa.TestScript.Suites/testscripts
for f in $(find . -name '*.json' | sed 's|^\./||'); do
  if [ ! -f "$NEW/$f" ]; then echo "MISSING $f";
  elif diff -q "$f" "$NEW/$f" >/dev/null; then echo "same    $f";
  else echo "DIFF    $f"; fi
done
```

Expected: exactly three `DIFF` lines — `Bundles/transaction.json`, `Search/chaining.json`,
`Validation/validate-op.json` — ten `same` lines, and zero `MISSING` lines. If this differs,
stop and re-derive the reconciliation set before continuing.

- [ ] **Step 2: Review each divergence**

```bash
cd /e/data/src/ignixa-fhir/conformance-tests
NEW=../src/Core/Ignixa.TestScript.Suites/testscripts
for f in Bundles/transaction.json Search/chaining.json Validation/validate-op.json; do
  echo "=================== $f"
  diff -u "$f" "$NEW/$f"
done
```

Expected: `Bundles/transaction.json` has 10 lines present only in the old tree,
`Search/chaining.json` has 12, `Validation/validate-op.json` has 1.

- [ ] **Step 3: Merge the old-only content into the new files**

For each of the three files, edit the copy under
`src/Core/Ignixa.TestScript.Suites/testscripts/` so it retains lab's additions **and** the
old-tree-only content from Step 2. Judgement call per hunk:

- An old-only `test` entry or `assert` that lab does not have: **keep it** — that is a lost assertion.
- An old-only line that lab deliberately replaced (e.g. a renamed test id, a reworded description, a corrected expectation): **take lab's version** and note which in the commit message.

Do not resolve by taking one side wholesale. Read each hunk.

- [ ] **Step 4: Verify every reconciled file still parses**

```powershell
cd E:\data\src\ignixa-fhir
foreach ($f in @(
  'src\Core\Ignixa.TestScript.Suites\testscripts\Bundles\transaction.json',
  'src\Core\Ignixa.TestScript.Suites\testscripts\Search\chaining.json',
  'src\Core\Ignixa.TestScript.Suites\testscripts\Validation\validate-op.json')) {
  try { Get-Content $f -Raw | ConvertFrom-Json | Out-Null; "ok   $f" }
  catch { "FAIL $f — $($_.Exception.Message)" }
}
```

Expected: three `ok` lines. This is a JSON syntax check only; Task 3 adds the parser-level
check across all 87. PowerShell is used here rather than `python`/`jq` because neither is
guaranteed present on this machine.

- [ ] **Step 5: Commit**

```bash
cd /e/data/src/ignixa-fhir
git add src/Core/Ignixa.TestScript.Suites/testscripts
git commit -m "fix(testscript): reconcile three divergent suites during consolidation

Bundles/transaction.json, Search/chaining.json and Validation/validate-op.json
had edits in both trees. Merged by hand so no assertion from the repo-root
copy is lost."
```

---

### Task 3: Point Ignixa.TestScript.Tests at the new location

The existing `ConformanceScriptParseTests` hand-rolls an ancestor walk for a repo-root `conformance-tests` directory. Replace it with `AppContext.BaseDirectory/testscripts`, populated by the imported targets file. This is the task that proves the in-repo delivery path works, and it raises parse coverage from 13 scripts to 87.

**Files:**
- Modify: `test/Ignixa.TestScript.Tests/Ignixa.TestScript.Tests.csproj`
- Modify: `test/Ignixa.TestScript.Tests/Conformance/ConformanceScriptParseTests.cs:8-40`

**Interfaces:**
- Consumes: `src/Core/Ignixa.TestScript.Suites/build/Ignixa.TestScript.Suites.targets` from Task 1.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Rewrite the test to resolve from the output directory**

Replace lines 8-40 of `test/Ignixa.TestScript.Tests/Conformance/ConformanceScriptParseTests.cs`
(the `LocateConformanceTestsRoot` method and the `ConformanceScriptFiles` member data) with:

```csharp
    private const string SuitesDirectoryName = "testscripts";

    public static IEnumerable<object[]> ConformanceScriptFiles()
    {
        var root = Path.Combine(AppContext.BaseDirectory, SuitesDirectoryName);
        if (!Directory.Exists(root))
            throw new InvalidOperationException(
                $"Conformance suites not found at '{root}'. They are copied to the output " +
                "directory by src/Core/Ignixa.TestScript.Suites/build/Ignixa.TestScript.Suites.targets, " +
                "which this project imports explicitly — check that the <Import> is still present. " +
                "A ProjectReference does not substitute: build/*.targets auto-import applies to " +
                "PackageReference only.");

        return Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Select(path => new object[] { path });
    }
```

Leave the `GivenConformanceScript_WhenParsing_ThenSucceedsWithNoErrorsOrWarnings` test body
and the `using` directives unchanged.

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd /e/data/src/ignixa-fhir
dotnet test test/Ignixa.TestScript.Tests/Ignixa.TestScript.Tests.csproj --filter "FullyQualifiedName~ConformanceScriptParseTests"
```

Expected: FAIL. `MemberData` throws `InvalidOperationException` with "Conformance suites not
found" — the targets file has not been imported yet, so nothing was copied to the output
directory.

- [ ] **Step 3: Import the suites targets**

Add this `<Import>` immediately before the closing `</Project>` tag of
`test/Ignixa.TestScript.Tests/Ignixa.TestScript.Tests.csproj`:

```xml
  <!--
    Explicit import, not a ProjectReference: NuGet auto-imports build/*.targets for
    PackageReference consumers only. This gives the in-repo test the same suite-delivery
    path that packaged consumers get, so a break in the targets file fails here rather
    than downstream after a publish.
  -->
  <Import Project="..\..\src\Core\Ignixa.TestScript.Suites\build\Ignixa.TestScript.Suites.targets" />
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
cd /e/data/src/ignixa-fhir
dotnet test test/Ignixa.TestScript.Tests/Ignixa.TestScript.Tests.csproj --filter "FullyQualifiedName~ConformanceScriptParseTests"
```

Expected: PASS, with **87 test cases per target framework** (the project multi-targets
`net9.0;net10.0`, so 174 total). If any script fails to parse, that is a real finding — the
script uses something the engine does not support. Record it, do not delete the script; see
"Expected fallout" in the spec.

- [ ] **Step 5: Verify the suites actually landed in the output directory**

```bash
cd /e/data/src/ignixa-fhir
find test/Ignixa.TestScript.Tests/bin/Debug/net10.0/testscripts -name '*.json' | wc -l
```

Expected: `87`.

- [ ] **Step 6: Commit**

```bash
cd /e/data/src/ignixa-fhir
git add test/Ignixa.TestScript.Tests
git commit -m "test(testscript): resolve conformance suites from output directory

Replaces the hand-rolled ancestor walk for repo-root conformance-tests
with AppContext.BaseDirectory/testscripts, populated by the Suites
targets file. The walk assumed a sibling-of-All.sln layout that is
fragile under git worktrees. Parse coverage goes 13 -> 87 scripts."
```

---

### Task 4: Point Ignixa.Api.E2ETests at the new location

**Files:**
- Modify: `test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj`
- Modify: `test/Ignixa.Api.E2ETests/Conformance/TestScriptConformanceReportTests.cs:39` and `:132-145`

**Interfaces:**
- Consumes: `src/Core/Ignixa.TestScript.Suites/build/Ignixa.TestScript.Suites.targets` from Task 1.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Add the suites directory constant**

In `test/Ignixa.Api.E2ETests/Conformance/TestScriptConformanceReportTests.cs`, add to the
constants block after line 20 (`private const string ImplementationName = "ignixa";`):

```csharp
    private const string SuitesDirectoryName = "testscripts";
```

- [ ] **Step 2: Resolve suites from the output directory**

Replace line 39:

```csharp
        var testsDirectory = FindRepositoryDirectory("conformance-tests");
```

with:

```csharp
        var testsDirectory = Path.Combine(AppContext.BaseDirectory, SuitesDirectoryName);
```

- [ ] **Step 3: Delete the now-unused repository walk**

Delete the entire `FindRepositoryDirectory` method, lines 132-145:

```csharp
    private static string FindRepositoryDirectory(string directoryName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, directoryName);
            if (Directory.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find '{directoryName}' from '{AppContext.BaseDirectory}'.");
    }
```

Leaving it would fail the build: `TreatWarningsAsErrors=true` plus `AnalysisLevel=latest-All`
turns the unused-private-member diagnostic into an error.

- [ ] **Step 4: Build to verify it fails**

```bash
cd /e/data/src/ignixa-fhir
dotnet build test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj -c Debug
```

Expected: build SUCCEEDS (the code compiles), but the suites are not yet copied. Confirm the
gap:

```bash
ls test/Ignixa.Api.E2ETests/bin/Debug/net10.0/testscripts 2>&1
```

Expected: "No such file or directory".

- [ ] **Step 5: Import the suites targets**

Add this `<Import>` immediately before the closing `</Project>` tag of
`test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj`:

```xml
  <!--
    Explicit import, not a ProjectReference: NuGet auto-imports build/*.targets for
    PackageReference consumers only. This gives the in-repo test the same suite-delivery
    path that packaged consumers get, so a break in the targets file fails here rather
    than downstream after a publish.
  -->
  <Import Project="..\..\src\Core\Ignixa.TestScript.Suites\build\Ignixa.TestScript.Suites.targets" />
```

- [ ] **Step 6: Rebuild and verify the suites land**

```bash
cd /e/data/src/ignixa-fhir
dotnet build test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj -c Debug
find test/Ignixa.Api.E2ETests/bin/Debug/net10.0/testscripts -name '*.json' | wc -l
```

Expected: build succeeds with 0 warnings; count is `87`.

- [ ] **Step 7: Commit**

```bash
cd /e/data/src/ignixa-fhir
git add test/Ignixa.Api.E2ETests
git commit -m "test(e2e): resolve conformance suites from output directory

Conformance report run now enumerates the canonical 87-script corpus
instead of the stale repo-root 13. Expect new failures on the next
IGNIXA_RUN_CONFORMANCE run; triage per the consolidation spec."
```

**Note:** the conformance run is gated behind `IGNIXA_RUN_CONFORMANCE=true` and needs the SQL
Server/Azurite E2E environment, so it no-ops in a normal local test run. Do not attempt to
run it here; the corpus expansion surfaces in CI/docs deployment.

---

### Task 5: Delete the stale repo-root corpus and update path references

Both consumers are rewired, so the old tree is now dead. Nothing reads it.

**Files:**
- Delete: `conformance-tests/` (13 files)
- Modify: `docs/adr/adr-2607-testscript-extensions.md` lines 11, 26, 49, 87, 104
- Modify: `docs/site/docs/core-sdk/testscript.md` lines 263, 284
- Modify: `tools/Ignixa.ConformanceMatrix.Cli/README.md` line 18
- Modify: `README.md`

**Interfaces:**
- Consumes: Tasks 3 and 4 must both be complete, or the deletion breaks the tests.
- Produces: nothing.

- [ ] **Step 1: Confirm nothing in the working tree still reads the old path**

```bash
cd /e/data/src/ignixa-fhir
grep -rn "conformance-tests" --include=*.cs --include=*.csproj --include=*.yml \
  --include=*.props --include=*.targets . 2>/dev/null \
  | grep -v '\.claude/worktrees' | grep -v '\.worktrees/' | grep -v '/bin/\|/obj/'
```

Expected: **no output**. Only Markdown references should remain. If a `.cs` or `.csproj` hit
appears, Task 3 or 4 is incomplete.

- [ ] **Step 2: Delete the stale tree**

```bash
cd /e/data/src/ignixa-fhir
git rm -r conformance-tests
```

- [ ] **Step 3: Update the ADR path references**

In `docs/adr/adr-2607-testscript-extensions.md`, replace `conformance-tests/` with
`src/Core/Ignixa.TestScript.Suites/testscripts/` at lines 11, 26, 49, 87 and 104. The
surrounding prose is unchanged; only the paths move. Specifically:

- Line 11: "...which runs `conformance-tests/` unattended in CI..." → "...which runs `src/Core/Ignixa.TestScript.Suites/testscripts/` unattended in CI..."
- Line 26: "From `conformance-tests/Search/intervals.json`:" → "From `src/Core/Ignixa.TestScript.Suites/testscripts/Search/intervals.json`:"
- Line 49: "From `conformance-tests/Search/string-modifiers.json`:" → "From `src/Core/Ignixa.TestScript.Suites/testscripts/Search/string-modifiers.json`:"
- Line 87: "From `conformance-tests/CRUD/basic.json`:" → "From `src/Core/Ignixa.TestScript.Suites/testscripts/CRUD/basic.json`:"
- Line 104: "...across source, tests, `conformance-tests/`, and `docs/`" → "...across source, tests, `src/Core/Ignixa.TestScript.Suites/testscripts/`, and `docs/`"

- [ ] **Step 4: Update the docs-site CLI example and report note**

In `docs/site/docs/core-sdk/testscript.md`, line 263:

```bash
ignixa-matrix run --server https://your-fhir-server --tests ./conformance-tests \
```

becomes:

```bash
ignixa-matrix run --server https://your-fhir-server --tests ./src/Core/Ignixa.TestScript.Suites/testscripts \
```

And line 284, replace:

```
- The report is generated during docs deployment by running the `conformance-tests` suite through the same SQL Server/Azurite-backed E2E test environment used by CI.
```

with:

```
- The report is generated during docs deployment by running the canonical suite corpus (`src/Core/Ignixa.TestScript.Suites/testscripts/`, also published as the `Ignixa.TestScript.Suites` package) through the same SQL Server/Azurite-backed E2E test environment used by CI.
```

- [ ] **Step 5: Update the CLI README example**

In `tools/Ignixa.ConformanceMatrix.Cli/README.md`, line 18, replace `./conformance-tests`
with `./src/Core/Ignixa.TestScript.Suites/testscripts`. The CLI itself takes `--tests <path>`
and needs no code change.

- [ ] **Step 6: Add a discoverability pointer to the root README**

Repo-root placement was the only thing advertising the corpus to someone landing on the
repo. Replace that with an explicit pointer. Add to `README.md`, in whichever section lists
testing or conformance (match the surrounding heading level and prose style):

```markdown
### Conformance suites

The canonical FHIR `TestScript` conformance corpus lives in
[`src/Core/Ignixa.TestScript.Suites/testscripts/`](src/Core/Ignixa.TestScript.Suites/testscripts/)
and is published as the [`Ignixa.TestScript.Suites`](https://www.nuget.org/packages/Ignixa.TestScript.Suites)
package so other servers can run it. Results are published to the
[conformance matrix](https://brendankowitz.github.io/ignixa-fhir/conformance/).
```

- [ ] **Step 7: Verify the build and affected tests still pass**

```bash
cd /e/data/src/ignixa-fhir
dotnet build All.sln -c Debug
dotnet test test/Ignixa.TestScript.Tests/Ignixa.TestScript.Tests.csproj --filter "FullyQualifiedName~ConformanceScriptParseTests"
```

Expected: build with 0 warnings and 0 errors; conformance parse tests PASS with 87 cases per
target framework.

- [ ] **Step 8: Commit**

```bash
cd /e/data/src/ignixa-fhir
git add -A
git commit -m "refactor(testscript): delete stale repo-root conformance-tests

Both consumers now resolve suites from the output directory, so the
13-script repo-root copy is dead. Path references updated in ADR 2607,
the docs site, and the matrix CLI README; root README gains an explicit
pointer to replace the discoverability repo-root placement provided."
```

---

### Task 6: Guard against extension drift

The failure this prevents: a suite authored against an engine capability that the shipped engine does not have. That is the class of drift that produced the current split, and neither the parse test nor the E2E run catches it cheaply — an unknown non-modifier extension is silently ignored per the FHIR spec.

**Files:**
- Create: `test/Ignixa.RepoGuards.Tests/ConformanceSuiteExtensionGuardTests.cs`

**Interfaces:**
- Consumes: the suite tree at `src/Core/Ignixa.TestScript.Suites/testscripts/`.
- Produces: nothing.

- [ ] **Step 1: Write the failing test**

Create `test/Ignixa.RepoGuards.Tests/ConformanceSuiteExtensionGuardTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Shouldly;
using Xunit;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// Guards the conformance corpus against extension drift (ADR 2607). Unknown non-modifier
/// extensions are silently ignored per the FHIR spec, so a suite authored against an engine
/// capability that does not ship would pass parsing and simply not do what its author
/// intended. This asserts every Ignixa-canonical extension URL used by a suite is one the
/// engine actually implements.
/// </summary>
public class ConformanceSuiteExtensionGuardTests
{
    private const string IgnixaExtensionPrefix = "http://ignixa.io/testscript/";

    private static readonly HashSet<string> KnownExtensionUrls = new(StringComparer.Ordinal)
    {
        "http://ignixa.io/testscript/parametrize",
        "http://ignixa.io/testscript/fhirVersions",
        "http://ignixa.io/testscript/requiresCapability",
        "http://ignixa.io/testscript/fhirfakes",
    };

    [Fact]
    public void GivenConformanceSuites_WhenReadingExtensionUrls_ThenAllAreImplementedByTheEngine()
    {
        var suiteFiles = EnumerateSuiteFiles().ToList();
        suiteFiles.ShouldNotBeEmpty("Expected to find conformance suites; scan path may be wrong.");

        var unknown = suiteFiles
            .SelectMany(file => CollectIgnixaExtensionUrls(file).Select(url => (file, url)))
            .Where(pair => !KnownExtensionUrls.Contains(pair.url))
            .Select(pair => $"{Path.GetFileName(pair.file)}: {pair.url}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        unknown.ShouldBeEmpty(
            "A suite uses an Ignixa TestScript extension the engine does not implement (ADR 2607). " +
            $"Known URLs: {string.Join(", ", KnownExtensionUrls)}. " +
            "Unknown non-modifier extensions are silently ignored, so this would not fail at runtime — " +
            "either implement the extension in Ignixa.TestScript and add it here, or fix the suite.");
    }

    private static IEnumerable<string> EnumerateSuiteFiles()
    {
        var suitesRoot = Path.Combine(
            FindRepoRoot(), "src", "Core", "Ignixa.TestScript.Suites", "testscripts");

        Directory.Exists(suitesRoot).ShouldBeTrue($"Expected conformance suites at {suitesRoot}.");
        return Directory.EnumerateFiles(suitesRoot, "*.json", SearchOption.AllDirectories);
    }

    private static IEnumerable<string> CollectIgnixaExtensionUrls(string filePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(filePath));
        return CollectFromElement(document.RootElement).ToList();
    }

    // Walks every "url" property in the document rather than only TestScript.extension[] and
    // test[].extension[]: the fhirfakes extension is placed inside the inline resource body
    // carried by fixture[].resource, so a shape-aware walk would miss it.
    private static IEnumerable<string> CollectFromElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "url" &&
                        property.Value.ValueKind == JsonValueKind.String &&
                        property.Value.GetString() is { } url &&
                        url.StartsWith(IgnixaExtensionPrefix, StringComparison.Ordinal))
                    {
                        yield return url;
                    }

                    foreach (var nested in CollectFromElement(property.Value))
                        yield return nested;
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in CollectFromElement(item))
                        yield return nested;
                }
                break;
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull($"Could not find repo root from {AppContext.BaseDirectory}");
        return dir!.FullName;
    }
}
```

- [ ] **Step 2: Run the test**

```bash
cd /e/data/src/ignixa-fhir
dotnet test test/Ignixa.RepoGuards.Tests/Ignixa.RepoGuards.Tests.csproj --filter "FullyQualifiedName~ConformanceSuiteExtensionGuardTests"
```

Expected: PASS. Unlike a normal TDD cycle there is no red step here — the guard asserts an
invariant that already holds, and its job is to keep holding. If it FAILS, that is a genuine
finding from lab's 74 newly-imported scripts: a suite uses an extension URL the engine does
not implement. Do not widen `KnownExtensionUrls` to make it pass — investigate the suite.

- [ ] **Step 3: Prove the guard actually detects drift**

Temporarily introduce a violation so the assertion is known to be live rather than vacuous:

```bash
cd /e/data/src/ignixa-fhir
sed -i 's|"http://ignixa.io/testscript/parametrize"|"http://ignixa.io/testscript/bogus"|' \
  src/Core/Ignixa.TestScript.Suites/testscripts/Search/intervals.json
dotnet test test/Ignixa.RepoGuards.Tests/Ignixa.RepoGuards.Tests.csproj --filter "FullyQualifiedName~ConformanceSuiteExtensionGuardTests"
```

Expected: FAIL, naming `intervals.json: http://ignixa.io/testscript/bogus`.

- [ ] **Step 4: Revert the temporary violation**

```bash
cd /e/data/src/ignixa-fhir
git checkout -- src/Core/Ignixa.TestScript.Suites/testscripts/Search/intervals.json
dotnet test test/Ignixa.RepoGuards.Tests/Ignixa.RepoGuards.Tests.csproj --filter "FullyQualifiedName~ConformanceSuiteExtensionGuardTests"
```

Expected: PASS, and `git status` shows no modification to `intervals.json`.

- [ ] **Step 5: Commit**

```bash
cd /e/data/src/ignixa-fhir
git add test/Ignixa.RepoGuards.Tests/ConformanceSuiteExtensionGuardTests.cs
git commit -m "test(guards): assert conformance suites use only implemented extensions

Unknown non-modifier extensions are silently ignored per the FHIR spec,
so a suite written against an unshipped capability passes parsing and
quietly does nothing. Walks every url property because the fhirfakes
extension lives inside fixture[].resource, not in an extension array."
```

---

### Task 7: Full-solution verification

**Files:** none modified.

**Interfaces:**
- Consumes: Tasks 1-6.
- Produces: nothing.

- [ ] **Step 1: Clean build the whole solution**

```bash
cd /e/data/src/ignixa-fhir
dotnet build All.sln -c Release
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Run the full unit test suite, excluding E2E**

```bash
cd /e/data/src/ignixa-fhir
dotnet test All.sln -c Release --filter "FullyQualifiedName!~E2ETests"
```

Expected: all tests PASS. `ConformanceScriptParseTests` contributes 87 cases per target
framework where it previously contributed 13.

- [ ] **Step 3: Confirm the pack step will pick up the new project**

CI packs by discovery, not by an explicit list, so verify the new project is in the glob:

```bash
cd /e/data/src/ignixa-fhir
find src/Core tools src/Application/Ignixa.Sidecar.Contracts -name "*.csproj" -type f | sort | grep Suites
```

Expected: `src/Core/Ignixa.TestScript.Suites/Ignixa.TestScript.Suites.csproj`. No workflow
edit is required — `ci.yml` uses exactly this `find`.

- [ ] **Step 4: Report status**

Summarise for the user: whether any of the 87 scripts failed to parse, whether the extension
guard surfaced anything, and confirm no `.github/workflows/**` file needed changing. Do not
claim the E2E conformance run passes — it is gated behind `IGNIXA_RUN_CONFORMANCE` and needs
the SQL Server/Azurite environment. Say plainly that it is unverified locally and will first
run in CI.

---

### Task 8: Freeze lab's tree (phase 2 prep)

The only change in this plan that touches `ignixa-lab`. Between phase 1 and phase 2 both copies exist and lab's is still writable — a suite authored there is lost work, because phase 2 deletes that tree wholesale. This is the only signal an author encounters at the moment they would create the file.

**Files:**
- Create: `E:\data\src\ignixa-lab\backend\src\Ignixa.Lab.Suites\README.md`

**Interfaces:**
- Consumes: nothing.
- Produces: nothing.

- [ ] **Step 1: Write the freeze note**

Create `E:\data\src\ignixa-lab\backend\src\Ignixa.Lab.Suites\README.md`:

```markdown
# Ignixa.Lab.Suites — FROZEN

**Do not add or edit suites here.** The canonical corpus moved to `ignixa-fhir` at
`src/Core/Ignixa.TestScript.Suites/testscripts/` and is published as the
`Ignixa.TestScript.Suites` package.

This project remains only until that package's first publish lands, at which point this
whole directory is deleted and replaced with a `PackageReference`. Anything added here in
the meantime is lost.

New or changed suites go to
[ignixa-fhir](https://github.com/brendankowitz/ignixa-fhir/tree/main/src/Core/Ignixa.TestScript.Suites/testscripts).

See `docs/superpowers/specs/2026-07-20-conformance-suite-consolidation-design.md` in
`ignixa-fhir` for the full plan.
```

- [ ] **Step 2: Verify lab still builds**

```bash
cd /e/data/src/ignixa-lab
dotnet build backend/src/Ignixa.Lab.Suites/Ignixa.Lab.Suites.csproj -c Debug
```

Expected: succeeds. The README is not packed (the csproj packs only `testscripts/**/*.json`
and the targets file) and the root `Directory.Build.props` `PackageReadmeFile` rule that
applies in `ignixa-fhir` does not apply in this repo — so adding it changes nothing about
the package.

- [ ] **Step 3: Commit in the lab repo**

```bash
cd /e/data/src/ignixa-lab
git add backend/src/Ignixa.Lab.Suites/README.md
git commit -m "docs(suites): freeze local suite tree pending upstream consolidation

The canonical corpus now lives in ignixa-fhir at
src/Core/Ignixa.TestScript.Suites/testscripts/. This tree is deleted and
replaced with a PackageReference once Ignixa.TestScript.Suites publishes.
Suites authored here in the interim are lost work."
```

- [ ] **Step 4: Open a tracking issue for phase 2**

Phase 2 must not be remembered only by a design document.

```bash
cd /e/data/src/ignixa-lab
gh issue create \
  --title "Repoint Ignixa.Lab.Suites at the published Ignixa.TestScript.Suites package" \
  --body "Phase 2 of the conformance suite consolidation (see \`docs/superpowers/specs/2026-07-20-conformance-suite-consolidation-design.md\` in ignixa-fhir).

Once \`Ignixa.TestScript.Suites\` has published from ignixa-fhir:

- Delete \`backend/src/Ignixa.Lab.Suites\`
- Add \`PackageReference Include=\"Ignixa.TestScript.Suites\"\` to the consuming project
- Add a dev-time path override so a local ignixa-fhir checkout's \`testscripts/\` wins over the package during suite authoring
- Verify \`Suites/SuiteCatalog.cs\` still resolves suites at \`AppContext.BaseDirectory/testscripts/\` — the folder name and package path were deliberately kept identical so this needs no change

Blocked until the first publish lands. Until then this tree is frozen (see its README)."
```

Expected: an issue URL is printed. If `gh` is not authenticated for this repo, create the
issue manually with the same title and body and report the URL.

---

## Self-Review

**Spec coverage:**

| Spec section | Task |
|---|---|
| Target layout (`src/Core/Ignixa.TestScript.Suites/`) | 1 |
| `testscripts/` name and `PackagePath` frozen | 1 (Global Constraints) |
| Packaging: PackageId, version, description, comments preserved | 1 |
| CI needs no workflow change | 7 Step 3 |
| Reconcile the 3 divergent files | 2 |
| Path resolution via `AppContext.BaseDirectory` | 3, 4 |
| Explicit `<Import>`, not `ProjectReference` | 3 Step 3, 4 Step 5, Global Constraints |
| Delete `FindRepositoryDirectory` / ancestor walk | 3 Step 1, 4 Step 3 |
| Delete repo-root `conformance-tests/` | 5 |
| Doc path updates (ADR ×5, docs site ×2, CLI README ×1) | 5 |
| README/docs-site discoverability pointer | 5 Step 6 |
| RepoGuards extension-URL guard | 6 |
| Expected fallout / triage guidance | 4 note, 7 Step 4 |
| Duplication window mitigation: freeze note | 8 |
| Duplication window mitigation: tracking issue | 8 Step 4 |
| Phase 2 (lab repoint) | Deliberately out of scope |

**Deferred to follow-up, not covered by a task:** the spec's "Follow-up" section calls for an ADR-2607 amendment recording the location decision and the `AppContext.BaseDirectory` mechanism. Task 5 updates ADR-2607's *paths* but does not add the decision record. That is a separate ADR edit and should be raised with the maintainer per the repo's ADR process rather than folded into an implementation commit.

**Type consistency:** `SuitesDirectoryName` is the constant name in both Task 3 and Task 4. `KnownExtensionUrls`, `IgnixaExtensionPrefix`, `CollectIgnixaExtensionUrls`, `CollectFromElement`, `EnumerateSuiteFiles` and `FindRepoRoot` are used consistently within Task 6. `FindRepoRoot` intentionally duplicates the helper in `PackageStabilityGuardTests` and `RuntimeMultiTargetingGuardTests` — matching the existing convention in that project, which already has two copies.

**Placeholder scan:** no TBD/TODO; every code step contains complete code; every command has an expected result.

# SQL on FHIR CLI Enhancements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend `ignixa-sqlonfhir` from a single-file tool to a batch-capable analytics pipeline — directory of ViewDefinitions × directory of flat FHIR, NDJSON output, FHIRPath variables, and progress reporting.

**Architecture:** `BatchProcessor` handles directory scan / resource-to-NDJSON matching / output naming as a static, file-path-only class (no I/O abstraction needed — tested with temp directories). Three commands — `run` (replaces `convert`), `preview`, `validate` — all auto-detect single vs batch mode by checking whether `--views` is a file or directory. `NdjsonFileWriter` mirrors `CsvFileWriter`; `VarParser` is a pure string utility.

**Tech Stack:** .NET 9, System.CommandLine, xunit + Shouldly, Parquet.Net (existing)

---

## File Structure

**New:**
- `src/Core/Ignixa.SqlOnFhir.Writers/NdjsonFileWriter.cs`
- `tools/Ignixa.SqlOnFhir.Cli/VarParser.cs`
- `tools/Ignixa.SqlOnFhir.Cli/Batch/BatchViewResult.cs`
- `tools/Ignixa.SqlOnFhir.Cli/Batch/BatchProcessor.cs`
- `tools/Ignixa.SqlOnFhir.Cli/Commands/RunCommand.cs`
- `test/Ignixa.SqlOnFhir.Cli.Tests/Ignixa.SqlOnFhir.Cli.Tests.csproj`
- `test/Ignixa.SqlOnFhir.Cli.Tests/NdjsonFileWriterTests.cs`
- `test/Ignixa.SqlOnFhir.Cli.Tests/VarParserTests.cs`
- `test/Ignixa.SqlOnFhir.Cli.Tests/BatchProcessorTests.cs`
- `test/Ignixa.SqlOnFhir.Cli.Tests/Integration/RunCommandIntegrationTests.cs`
- `test/Ignixa.SqlOnFhir.Cli.Tests/Integration/PreviewCommandIntegrationTests.cs`
- `test/Ignixa.SqlOnFhir.Cli.Tests/Integration/ValidateCommandIntegrationTests.cs`
- `test/Ignixa.SqlOnFhir.Cli.Tests/Fixtures/views/patient-demographics.json`
- `test/Ignixa.SqlOnFhir.Cli.Tests/Fixtures/views/observation-codes.json`
- `test/Ignixa.SqlOnFhir.Cli.Tests/Fixtures/fhir/Patient.ndjson`
- `test/Ignixa.SqlOnFhir.Cli.Tests/Fixtures/fhir/Observation.ndjson`

**Modified:**
- `src/Core/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirEvaluator.cs` — add optional `variables` parameter
- `src/Core/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirEvaluationVisitor.cs` — thread variables into `EvaluationContext`
- `tools/Ignixa.SqlOnFhir.Cli/Program.cs` — register `run`; remove `convert`; align `preview`/`validate`
- `tools/Ignixa.SqlOnFhir.Cli/Commands/PreviewCommand.cs` — optional `--input`, `--pattern`, dir mode
- `tools/Ignixa.SqlOnFhir.Cli/Commands/ValidateCommand.cs` — `--pattern`, dir mode, summary table

**Removed:**
- `tools/Ignixa.SqlOnFhir.Cli/Commands/ConvertCommand.cs`

---

### Task 1: Create test project

**Files:**
- Create: `test/Ignixa.SqlOnFhir.Cli.Tests/Ignixa.SqlOnFhir.Cli.Tests.csproj`

- [ ] **Step 1: Create the .csproj**

```xml
<!-- test/Ignixa.SqlOnFhir.Cli.Tests/Ignixa.SqlOnFhir.Cli.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Shouldly" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\tools\Ignixa.SqlOnFhir.Cli\Ignixa.SqlOnFhir.Cli.csproj" />
    <ProjectReference Include="..\..\src\Core\Ignixa.SqlOnFhir.Writers\Ignixa.SqlOnFhir.Writers.csproj" />
    <ProjectReference Include="..\..\src\Core\Ignixa.SqlOnFhir\Ignixa.SqlOnFhir.csproj" />
    <ProjectReference Include="..\..\src\Core\Ignixa.Specification\Ignixa.Specification.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add to solution**

```bash
dotnet sln All.sln add test/Ignixa.SqlOnFhir.Cli.Tests/Ignixa.SqlOnFhir.Cli.Tests.csproj
```

- [ ] **Step 3: Verify build**

```bash
dotnet build test/Ignixa.SqlOnFhir.Cli.Tests/Ignixa.SqlOnFhir.Cli.Tests.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add test/Ignixa.SqlOnFhir.Cli.Tests/Ignixa.SqlOnFhir.Cli.Tests.csproj All.sln
git commit -m "test(sqlonfhir-cli): scaffold test project"
```

---

### Task 2: NdjsonFileWriter

**Files:**
- Create: `src/Core/Ignixa.SqlOnFhir.Writers/NdjsonFileWriter.cs`
- Create: `test/Ignixa.SqlOnFhir.Cli.Tests/NdjsonFileWriterTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// test/Ignixa.SqlOnFhir.Cli.Tests/NdjsonFileWriterTests.cs
using System.Text.Json;
using Ignixa.SqlOnFhir.Writers;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.SqlOnFhir.Cli.Tests;

public class NdjsonFileWriterTests : IAsyncDisposable
{
    private readonly string _outputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.ndjson");

    [Fact]
    public async Task GivenSingleRow_WhenWritten_ThenFileContainsValidJsonLine()
    {
        await using var writer = new NdjsonFileWriter(_outputPath, NullLogger.Instance);
        var row = new Dictionary<string, object?> { ["id"] = "p1", ["name"] = "Smith" };

        await writer.WriteRowAsync(row);
        await writer.FlushAsync();

        var lines = await File.ReadAllLinesAsync(_outputPath);
        lines.Length.ShouldBe(1);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(lines[0])!;
        parsed["id"].GetString().ShouldBe("p1");
        parsed["name"].GetString().ShouldBe("Smith");
    }

    [Fact]
    public async Task GivenMultipleRows_WhenWritten_ThenEachRowIsASeparateLine()
    {
        await using var writer = new NdjsonFileWriter(_outputPath, NullLogger.Instance);

        await writer.WriteRowAsync(new Dictionary<string, object?> { ["id"] = "p1" });
        await writer.WriteRowAsync(new Dictionary<string, object?> { ["id"] = "p2" });
        await writer.WriteRowAsync(new Dictionary<string, object?> { ["id"] = "p3" });
        await writer.FlushAsync();

        var lines = (await File.ReadAllLinesAsync(_outputPath))
            .Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        lines.Length.ShouldBe(3);
        writer.RowsWritten.ShouldBe(3);
    }

    [Fact]
    public async Task GivenNullValue_WhenWritten_ThenFieldIsJsonNull()
    {
        await using var writer = new NdjsonFileWriter(_outputPath, NullLogger.Instance);

        await writer.WriteRowAsync(new Dictionary<string, object?> { ["id"] = "p1", ["name"] = null });
        await writer.FlushAsync();

        var lines = await File.ReadAllLinesAsync(_outputPath);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(lines[0])!;
        parsed["name"].ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GivenRows_WhenFlushed_ThenBytesWrittenIsPositive()
    {
        await using var writer = new NdjsonFileWriter(_outputPath, NullLogger.Instance);

        await writer.WriteRowAsync(new Dictionary<string, object?> { ["id"] = "p1" });
        await writer.FlushAsync();

        writer.BytesWritten.ShouldBeGreaterThan(0);
    }

    public async ValueTask DisposeAsync()
    {
        if (File.Exists(_outputPath)) File.Delete(_outputPath);
    }
}
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test test/Ignixa.SqlOnFhir.Cli.Tests/Ignixa.SqlOnFhir.Cli.Tests.csproj --filter "NdjsonFileWriterTests" -v minimal
```
Expected: Build error — `NdjsonFileWriter` not found.

- [ ] **Step 3: Implement NdjsonFileWriter**

```csharp
// src/Core/Ignixa.SqlOnFhir.Writers/NdjsonFileWriter.cs
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Ignixa.SqlOnFhir.Writers;

public partial class NdjsonFileWriter : IAsyncDisposable
{
    private readonly string _outputPath;
    private readonly ILogger _logger;
    private StreamWriter? _writer;
    private bool _disposed;
    private long _rowsWritten;

    public long BytesWritten { get; private set; }
    public long RowsWritten => _rowsWritten;

    public NdjsonFileWriter(string outputPath, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(logger);
        _outputPath = outputPath;
        _logger = logger;
    }

    public async Task WriteRowAsync(Dictionary<string, object?> row, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _writer ??= new StreamWriter(File.Create(_outputPath), Encoding.UTF8);
        var json = JsonSerializer.Serialize(row);
        await _writer.WriteLineAsync(json.AsMemory(), cancellationToken);
        _rowsWritten++;
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_writer != null)
        {
            await _writer.FlushAsync(cancellationToken);
            var fileInfo = new FileInfo(_outputPath);
            BytesWritten = fileInfo.Exists ? fileInfo.Length : 0;
            LogNdjsonFileWritten(_logger, _rowsWritten, BytesWritten, _outputPath);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try { await FlushAsync(CancellationToken.None); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error during final flush on dispose"); }
        finally
        {
            if (_writer != null) await _writer.DisposeAsync();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, GetType());

    [LoggerMessage(Level = LogLevel.Debug, Message = "Wrote NDJSON file with {RowsWritten} rows ({BytesWritten} bytes) to: {OutputPath}")]
    private static partial void LogNdjsonFileWritten(ILogger logger, long rowsWritten, long bytesWritten, string outputPath);
}
```

- [ ] **Step 4: Run to verify passing**

```bash
dotnet test test/Ignixa.SqlOnFhir.Cli.Tests/Ignixa.SqlOnFhir.Cli.Tests.csproj --filter "NdjsonFileWriterTests" -v minimal
```
Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Ignixa.SqlOnFhir.Writers/NdjsonFileWriter.cs test/Ignixa.SqlOnFhir.Cli.Tests/NdjsonFileWriterTests.cs
git commit -m "feat(sqlonfhir-cli): add NdjsonFileWriter"
```

---

### Task 3: VarParser

**Files:**
- Create: `tools/Ignixa.SqlOnFhir.Cli/VarParser.cs`
- Create: `test/Ignixa.SqlOnFhir.Cli.Tests/VarParserTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// test/Ignixa.SqlOnFhir.Cli.Tests/VarParserTests.cs
using Ignixa.SqlOnFhir.Cli;
using Shouldly;

namespace Ignixa.SqlOnFhir.Cli.Tests;

public class VarParserTests
{
    [Fact]
    public void GivenNullInput_WhenParsing_ThenReturnsEmpty()
        => VarParser.Parse(null).ShouldBeEmpty();

    [Fact]
    public void GivenValidPairs_WhenParsing_ThenReturnsDictionary()
    {
        var result = VarParser.Parse(["effectiveDate=2024-01-01", "cohortId=COHORT_A"]);
        result["effectiveDate"].ShouldBe("2024-01-01");
        result["cohortId"].ShouldBe("COHORT_A");
    }

    [Fact]
    public void GivenValueContainingEquals_WhenParsing_ThenUsesFirstEqualsAsDelimiter()
    {
        var result = VarParser.Parse(["url=http://example.com/path?a=b"]);
        result["url"].ShouldBe("http://example.com/path?a=b");
    }

    [Fact]
    public void GivenMissingEquals_WhenParsing_ThenThrowsArgumentException()
        => Should.Throw<ArgumentException>(() => VarParser.Parse(["noequals"]));

    [Fact]
    public void GivenEmptyName_WhenParsing_ThenThrowsArgumentException()
        => Should.Throw<ArgumentException>(() => VarParser.Parse(["=value"]));

    [Fact]
    public void GivenEmptyValue_WhenParsing_ThenValueIsEmptyString()
        => VarParser.Parse(["name="]) ["name"].ShouldBe("");
}
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test test/Ignixa.SqlOnFhir.Cli.Tests/Ignixa.SqlOnFhir.Cli.Tests.csproj --filter "VarParserTests" -v minimal
```
Expected: Build error — `VarParser` not found.

- [ ] **Step 3: Implement VarParser**

```csharp
// tools/Ignixa.SqlOnFhir.Cli/VarParser.cs
namespace Ignixa.SqlOnFhir.Cli;

internal static class VarParser
{
    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string>? vars)
    {
        if (vars is null)
            return new Dictionary<string, string>();

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var v in vars)
        {
            var idx = v.IndexOf('=', StringComparison.Ordinal);
            if (idx <= 0)
                throw new ArgumentException($"Invalid --var format '{v}'. Expected name=value.");
            result[v[..idx]] = v[(idx + 1)..];
        }
        return result;
    }
}
```

- [ ] **Step 4: Run to verify passing**

```bash
dotnet test test/Ignixa.SqlOnFhir.Cli.Tests/Ignixa.SqlOnFhir.Cli.Tests.csproj --filter "VarParserTests" -v minimal
```
Expected: 6 passed.

- [ ] **Step 5: Commit**

```bash
git add tools/Ignixa.SqlOnFhir.Cli/VarParser.cs test/Ignixa.SqlOnFhir.Cli.Tests/VarParserTests.cs
git commit -m "feat(sqlonfhir-cli): add VarParser for --var flag"
```

---

### Task 4: Thread variables through SqlOnFhirEvaluator

**Files:**
- Modify: `src/Core/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirEvaluationVisitor.cs`
- Modify: `src/Core/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirEvaluator.cs`

The visitor creates an `EvaluationContext` in `CreateEvaluationContext`. This task adds an optional `variables` parameter so `--var name=value` pairs become `%name` in FHIRPath expressions, using the same `WithEnvironmentVariable` call the existing constants already use.

- [ ] **Step 1: Extend `SqlOnFhirEvaluationVisitor` to accept external variables**

In `SqlOnFhirEvaluationVisitor.cs`, change the public `Evaluate` overload and private `CreateEvaluationContext` to accept an optional variables dictionary:

```csharp
// Replace the existing public Evaluate method and private CreateEvaluationContext:

public IEnumerable<Dictionary<string, object?>> Evaluate(
    ViewDefinitionExpression viewDef,
    IElement resource,
    IReadOnlyDictionary<string, string>? variables = null)
{
    var context = CreateEvaluationContext(viewDef, resource, variables);
    return EvaluateViewDefinition(viewDef, resource, context);
}

private static EvaluationContext CreateEvaluationContext(
    ViewDefinitionExpression viewDef,
    IElement resource,
    IReadOnlyDictionary<string, string>? variables)
{
    var context = new EvaluationContext() with { RootResource = resource };
    foreach (var constant in viewDef.Constants)
        if (constant.Value != null)
            context = context.WithEnvironmentVariable(constant.Name, new PrimitiveValueElement(constant.Value));
    if (variables != null)
        foreach (var (name, value) in variables)
            context = context.WithEnvironmentVariable(name, new PrimitiveValueElement(value));
    // rowIndex injected last so it cannot be shadowed by user-defined constants or variables
    context = context.WithEnvironmentVariable("rowIndex", new PrimitiveValueElement(0));
    return context;
}
```

- [ ] **Step 2: Extend `SqlOnFhirEvaluator.Evaluate` to forward variables**

In `SqlOnFhirEvaluator.cs`, add the optional parameter to the public method and forward it to `_visitor.Evaluate`:

```csharp
// Replace the existing public Evaluate method:

public IEnumerable<Dictionary<string, object?>> Evaluate(
    ISourceNavigator viewDefinitionNode,
    IElement resource,
    IReadOnlyDictionary<string, string>? variables = null)
{
    ArgumentNullException.ThrowIfNull(viewDefinitionNode);
    ArgumentNullException.ThrowIfNull(resource);

    try
    {
        var resourceType = viewDefinitionNode.Children("resource").FirstOrDefault()?.Text ?? "Unknown";
        var cacheKey = $"{resourceType}_{viewDefinitionNode.GetHashCode()}";

        if (!_compiledViewDefinitions.TryGetValue(cacheKey, out var viewExpr))
        {
            viewExpr = ViewDefinitionExpressionParser.Parse(viewDefinitionNode);
            _compiledViewDefinitions[cacheKey] = viewExpr;
        }

        return _visitor.Evaluate(viewExpr, resource, variables);
    }
    catch (Exception ex)
    {
        var resourceType = viewDefinitionNode.Children("resource").FirstOrDefault()?.Text ?? "Unknown";
        throw new InvalidOperationException(
            $"Failed to evaluate ViewDefinition for resource type '{resourceType}'",
            ex);
    }
}
```

- [ ] **Step 3: Build core library**

```bash
dotnet build src/Core/Ignixa.SqlOnFhir/Ignixa.SqlOnFhir.csproj
```
Expected: 0 errors.

- [ ] **Step 4: Run existing SqlOnFhir tests to verify nothing broke**

```bash
dotnet test test/Ignixa.SqlOnFhir.Tests/Ignixa.SqlOnFhir.Tests.csproj -v minimal
```
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirEvaluator.cs src/Core/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirEvaluationVisitor.cs
git commit -m "feat(sqlonfhir): add optional variables parameter to evaluator"
```

---

### Task 5: BatchViewResult and BatchProcessor

**Files:**
- Create: `tools/Ignixa.SqlOnFhir.Cli/Batch/BatchViewResult.cs`
- Create: `tools/Ignixa.SqlOnFhir.Cli/Batch/BatchProcessor.cs`
- Create: `test/Ignixa.SqlOnFhir.Cli.Tests/BatchProcessorTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// test/Ignixa.SqlOnFhir.Cli.Tests/BatchProcessorTests.cs
using Ignixa.SqlOnFhir.Cli.Batch;
using Shouldly;

namespace Ignixa.SqlOnFhir.Cli.Tests;

public class BatchProcessorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public BatchProcessorTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public void GivenDirectory_WhenDiscoveringViews_ThenReturnsJsonFilesOnly()
    {
        File.WriteAllText(Path.Combine(_tempDir, "patient.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "obs.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "readme.md"), "ignore");

        var files = BatchProcessor.DiscoverViewDefinitions(_tempDir, "**/*.json").ToList();

        files.Count.ShouldBe(2);
        files.ShouldAllBe(f => f.EndsWith(".json"));
    }

    [Fact]
    public void GivenSubdirectory_WhenDiscoveringViews_ThenSearchesRecursively()
    {
        var sub = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "nested.json"), "{}");

        var files = BatchProcessor.DiscoverViewDefinitions(_tempDir, "**/*.json").ToList();

        files.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenInputDirectory_WhenFindingFiles_ThenMatchesByResourceName()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Patient.ndjson"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "Observation.ndjson"), "{}");

        var files = BatchProcessor.FindInputFiles(_tempDir, "Patient", "*{resource}*.ndjson").ToList();

        files.Count.ShouldBe(1);
        Path.GetFileName(files[0]).ShouldBe("Patient.ndjson");
    }

    [Fact]
    public void GivenLowercaseFile_WhenFindingFiles_ThenMatchIsCaseInsensitive()
    {
        File.WriteAllText(Path.Combine(_tempDir, "patient.ndjson"), "{}");

        var files = BatchProcessor.FindInputFiles(_tempDir, "Patient", "*{resource}*.ndjson").ToList();

        files.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenShardedFiles_WhenFindingFiles_ThenReturnsAllInLexicographicOrder()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Patient_1.ndjson"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "Patient_2.ndjson"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "Patient_3.ndjson"), "{}");

        var files = BatchProcessor.FindInputFiles(_tempDir, "Patient", "*{resource}*.ndjson").ToList();

        files.Count.ShouldBe(3);
        files.ShouldBeInOrder();
    }

    [Fact]
    public void GivenNoMatchingFiles_WhenFindingFiles_ThenReturnsEmpty()
        => BatchProcessor.FindInputFiles(_tempDir, "AllergyIntolerance", "*{resource}*.ndjson").ShouldBeEmpty();

    [Fact]
    public void GivenViewDefinitionPath_WhenGettingOutputPath_ThenUsesBasenameAndFormat()
    {
        var outPath = BatchProcessor.GetOutputPath(
            Path.Combine(Path.GetTempPath(), "output"),
            Path.Combine(Path.GetTempPath(), "views", "patient-demographics.json"),
            "parquet");

        Path.GetFileName(outPath).ShouldBe("patient-demographics.parquet");
    }

    [Fact]
    public void GivenMultipleViewFiles_WhenDiscovering_ThenReturnedInLexicographicOrder()
    {
        File.WriteAllText(Path.Combine(_tempDir, "z-view.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "a-view.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "m-view.json"), "{}");

        var files = BatchProcessor.DiscoverViewDefinitions(_tempDir, "**/*.json")
            .Select(Path.GetFileName).ToList();

        files.ShouldBe(["a-view.json", "m-view.json", "z-view.json"]);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);
}
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test test/Ignixa.SqlOnFhir.Cli.Tests/Ignixa.SqlOnFhir.Cli.Tests.csproj --filter "BatchProcessorTests" -v minimal
```
Expected: Build error — `BatchProcessor` not found.

- [ ] **Step 3: Create BatchViewResult**

```csharp
// tools/Ignixa.SqlOnFhir.Cli/Batch/BatchViewResult.cs
namespace Ignixa.SqlOnFhir.Cli.Batch;

internal record BatchViewResult(
    string ViewDefinitionName,
    BatchViewStatus Status,
    long RowsWritten = 0,
    long BytesWritten = 0,
    double DurationSeconds = 0,
    string? OutputPath = null,
    string? SkipReason = null,
    string? ErrorMessage = null);

internal enum BatchViewStatus { Completed, Skipped, Failed }
```

- [ ] **Step 4: Create BatchProcessor**

```csharp
// tools/Ignixa.SqlOnFhir.Cli/Batch/BatchProcessor.cs
namespace Ignixa.SqlOnFhir.Cli.Batch;

internal static class BatchProcessor
{
    public static IEnumerable<string> DiscoverViewDefinitions(string viewsDir, string pattern)
    {
        var filePattern = StripLeadingGlobPrefix(pattern);
        return Directory.EnumerateFiles(viewsDir, filePattern, SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
    }

    public static IEnumerable<string> FindInputFiles(string inputDir, string resource, string inputPattern)
    {
        var filePattern = inputPattern.Replace("{resource}", resource, StringComparison.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(inputDir, filePattern, SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
    }

    public static string GetOutputPath(string outputDir, string viewDefinitionPath, string format)
    {
        var basename = Path.GetFileNameWithoutExtension(viewDefinitionPath);
        return Path.Combine(outputDir, $"{basename}.{format}");
    }

    private static string StripLeadingGlobPrefix(string pattern)
    {
        if (pattern.StartsWith("**/", StringComparison.Ordinal) ||
            pattern.StartsWith("**\\", StringComparison.Ordinal))
            return pattern[3..];
        return pattern;
    }
}
```

- [ ] **Step 5: Run to verify passing**

```bash
dotnet test test/Ignixa.SqlOnFhir.Cli.Tests/Ignixa.SqlOnFhir.Cli.Tests.csproj --filter "BatchProcessorTests" -v minimal
```
Expected: 8 passed.

- [ ] **Step 6: Commit**

```bash
git add tools/Ignixa.SqlOnFhir.Cli/Batch/ test/Ignixa.SqlOnFhir.Cli.Tests/BatchProcessorTests.cs
git commit -m "feat(sqlonfhir-cli): add BatchProcessor and BatchViewResult"
```

---

### Task 6: RunCommand

**Files:**
- Create: `tools/Ignixa.SqlOnFhir.Cli/Commands/RunCommand.cs`
- Delete: `tools/Ignixa.SqlOnFhir.Cli/Commands/ConvertCommand.cs`

`RunCommand` replaces `ConvertCommand`. Single-file mode mirrors existing convert behaviour exactly. Batch mode uses `BatchProcessor` for discovery, matching, and output naming; writes one output file per ViewDefinition; reports progress; supports `--stats-out`.

- [ ] **Step 1: Create RunCommand.cs**

```csharp
// tools/Ignixa.SqlOnFhir.Cli/Commands/RunCommand.cs
using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.SqlOnFhir.Cli.Batch;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Parsing;
using Ignixa.SqlOnFhir.Writers;
using Microsoft.Extensions.Logging.Abstractions;
using Parquet.Data;
using Parquet.Schema;

namespace Ignixa.SqlOnFhir.Cli.Commands;

internal static class RunCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider, string fhirVersion)
    {
        var cmd = new Command("run", "Convert FHIR resources using ViewDefinition(s). Accepts file or directory for --views and --input.");

        var viewsOpt      = new Option<string>("--views")         { Description = "ViewDefinition file or directory",                    Required = true };
        var inputOpt      = new Option<string>("--input")         { Description = "NDJSON file or directory",                            Required = true };
        var outOpt        = new Option<string>("--out")           { Description = "Output file (single mode) or directory (batch mode)", Required = true };
        var formatOpt     = new Option<string>("--format")        { Description = "Batch mode output format: parquet, csv, ndjson (default: parquet)",          DefaultValueFactory = _ => "parquet" };
        var patternOpt    = new Option<string>("--pattern")       { Description = "ViewDefinition glob, batch mode only (default: **/*.json)",                  DefaultValueFactory = _ => "**/*.json" };
        var inputPatOpt   = new Option<string>("--input-pattern") { Description = "NDJSON match pattern, batch mode only (default: *{resource}*.ndjson)",       DefaultValueFactory = _ => "*{resource}*.ndjson" };
        var varOpt        = new Option<string[]>("--var")         { Description = "FHIRPath variable name=value, repeatable", AllowMultipleArgumentsPerToken = false };
        var quietOpt      = new Option<bool>("--quiet")           { Description = "Suppress all console output" };
        var statsOutOpt   = new Option<string?>("--stats-out")    { Description = "Write JSON stats summary to file" };

        cmd.Options.Add(viewsOpt);
        cmd.Options.Add(inputOpt);
        cmd.Options.Add(outOpt);
        cmd.Options.Add(formatOpt);
        cmd.Options.Add(patternOpt);
        cmd.Options.Add(inputPatOpt);
        cmd.Options.Add(varOpt);
        cmd.Options.Add(quietOpt);
        cmd.Options.Add(statsOutOpt);

        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var views        = parseResult.GetValue(viewsOpt)!;
            var input        = parseResult.GetValue(inputOpt)!;
            var out_         = parseResult.GetValue(outOpt)!;
            var format       = parseResult.GetValue(formatOpt)!;
            var pattern      = parseResult.GetValue(patternOpt)!;
            var inputPattern = parseResult.GetValue(inputPatOpt)!;
            var vars         = VarParser.Parse(parseResult.GetValue(varOpt));
            var quiet        = parseResult.GetValue(quietOpt);
            var statsOut     = parseResult.GetValue(statsOutOpt);

            if (Directory.Exists(views))
                await RunBatch(schemaProvider, fhirVersion, views, input, out_, format, pattern, inputPattern, vars, quiet, statsOut, cancellationToken);
            else
                await RunSingle(schemaProvider, fhirVersion, views, input, out_, vars, quiet, cancellationToken);
        });

        return cmd;
    }

    private static async Task RunSingle(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string viewsPath,
        string inputPath,
        string outputPath,
        IReadOnlyDictionary<string, string> vars,
        bool quiet,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var format = DetectFormat(outputPath);
            if (format is null) { Print(quiet, "✗ Unsupported output extension. Use .parquet, .csv, or .ndjson"); Environment.ExitCode = 1; return; }
            if (!File.Exists(viewsPath)) { Print(quiet, $"✗ ViewDefinition not found: {viewsPath}"); Environment.ExitCode = 1; return; }
            if (!File.Exists(inputPath)) { Print(quiet, $"✗ Input not found: {inputPath}"); Environment.ExitCode = 1; return; }

            var viewNav = ParseViewDefinition(viewsPath);
            if (viewNav is null) { Print(quiet, "✗ Failed to parse ViewDefinition"); Environment.ExitCode = 1; return; }

            var schemaEval = new SqlOnFhirSchemaEvaluator();
            var viewExpr   = ViewDefinitionExpressionParser.Parse(viewNav);
            var colSchemas = schemaEval.GetSchema(viewExpr);
            Print(quiet, $"✓ Using FHIR {fhirVersion.ToUpperInvariant()} — {colSchemas.Count} columns");

            var evaluator = new SqlOnFhirEvaluator();
            var rows = await WriteOutputAsync(outputPath, format, [inputPath], viewNav, colSchemas, schemaProvider, evaluator, vars, cancellationToken);
            var bytes = new FileInfo(outputPath).Exists ? new FileInfo(outputPath).Length : 0L;
            Print(quiet, $"✓ {rows:N0} rows → {outputPath} ({bytes:N0} bytes) in {sw.Elapsed.TotalSeconds:F1}s");
        }
        catch (Exception ex)
        {
            Print(quiet, $"✗ Error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            Environment.ExitCode = 1;
        }
    }

    private static async Task RunBatch(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string viewsDir,
        string inputDir,
        string outputDir,
        string format,
        string pattern,
        string inputPattern,
        IReadOnlyDictionary<string, string> vars,
        bool quiet,
        string? statsOut,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(inputDir)) { Print(quiet, $"✗ Input directory not found: {inputDir}"); Environment.ExitCode = 1; return; }

        Directory.CreateDirectory(outputDir);

        var viewFiles = BatchProcessor.DiscoverViewDefinitions(viewsDir, pattern).ToList();
        if (viewFiles.Count == 0) { Print(quiet, $"✗ No ViewDefinition files found matching '{pattern}' in {viewsDir}"); Environment.ExitCode = 1; return; }

        Print(quiet, $"✓ Found {viewFiles.Count} ViewDefinition(s)  [{fhirVersion.ToUpperInvariant()}]\n");

        var results   = new List<BatchViewResult>();
        var evaluator = new SqlOnFhirEvaluator();

        for (var i = 0; i < viewFiles.Count; i++)
        {
            var vdPath = viewFiles[i];
            var vdName = Path.GetFileNameWithoutExtension(vdPath);
            var sw     = Stopwatch.StartNew();

            var viewNav = ParseViewDefinition(vdPath);
            if (viewNav is null)
            {
                Print(quiet, $"  [{i + 1}/{viewFiles.Count}] {vdName}  → skipped (parse error)");
                results.Add(new BatchViewResult(vdName, BatchViewStatus.Skipped, SkipReason: "Failed to parse ViewDefinition JSON"));
                continue;
            }

            var resource = viewNav.Children("resource").FirstOrDefault()?.Text ?? string.Empty;
            if (string.IsNullOrEmpty(resource))
            {
                Print(quiet, $"  [{i + 1}/{viewFiles.Count}] {vdName}  → skipped (missing resource field)");
                results.Add(new BatchViewResult(vdName, BatchViewStatus.Skipped, SkipReason: "ViewDefinition missing 'resource' field"));
                continue;
            }

            var inputFiles = BatchProcessor.FindInputFiles(inputDir, resource, inputPattern).ToList();
            if (inputFiles.Count == 0)
            {
                Print(quiet, $"  [{i + 1}/{viewFiles.Count}] {vdName}  → skipped (no {resource} NDJSON in {inputDir})");
                results.Add(new BatchViewResult(vdName, BatchViewStatus.Skipped, SkipReason: $"No NDJSON files matching '{resource}'"));
                continue;
            }

            var outputPath = BatchProcessor.GetOutputPath(outputDir, vdPath, format);

            try
            {
                var schemaEval = new SqlOnFhirSchemaEvaluator();
                var viewExpr   = ViewDefinitionExpressionParser.Parse(viewNav);
                var colSchemas = schemaEval.GetSchema(viewExpr);
                var rows  = await WriteOutputAsync(outputPath, format, inputFiles, viewNav, colSchemas, schemaProvider, evaluator, vars, cancellationToken);
                var bytes = new FileInfo(outputPath).Exists ? new FileInfo(outputPath).Length : 0L;
                sw.Stop();

                Print(quiet, $"  [{i + 1}/{viewFiles.Count}] {vdName,-40}  {rows,8:N0} rows  {Path.GetFileName(outputPath)} ({bytes / 1_048_576.0:F1} MB)  {sw.Elapsed.TotalSeconds:F1}s");
                results.Add(new BatchViewResult(vdName, BatchViewStatus.Completed, rows, bytes, sw.Elapsed.TotalSeconds, outputPath));
            }
            catch (Exception ex)
            {
                Print(quiet, $"  [{i + 1}/{viewFiles.Count}] {vdName}  → ERROR: {ex.Message}");
                results.Add(new BatchViewResult(vdName, BatchViewStatus.Failed, ErrorMessage: ex.Message));
            }
        }

        var completed = results.Count(r => r.Status == BatchViewStatus.Completed);
        var skipped   = results.Count(r => r.Status == BatchViewStatus.Skipped);
        var failed    = results.Count(r => r.Status == BatchViewStatus.Failed);
        Print(quiet, $"\n✓ Done: {completed} completed, {skipped} skipped, {failed} failed");

        if (statsOut is not null)
        {
            var stats = new
            {
                total = results.Count,
                completed,
                skipped,
                failed,
                views = results.Select(r => new
                {
                    name            = r.ViewDefinitionName,
                    status          = r.Status.ToString().ToLowerInvariant(),
                    rows            = r.RowsWritten,
                    bytes           = r.BytesWritten,
                    durationSeconds = r.DurationSeconds,
                    skipReason      = r.SkipReason,
                    errorMessage    = r.ErrorMessage
                })
            };
            await File.WriteAllTextAsync(statsOut,
                JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
        }

        if (completed == 0)
            Environment.ExitCode = 1;
    }

    private static async Task<long> WriteOutputAsync(
        string outputPath,
        string format,
        List<string> inputFiles,
        ISourceNavigator viewNav,
        IReadOnlyList<ColumnSchema> colSchemas,
        IFhirSchemaProvider schemaProvider,
        SqlOnFhirEvaluator evaluator,
        IReadOnlyDictionary<string, string> vars,
        CancellationToken cancellationToken)
    {
        var rows = 0L;
        if (format == "parquet")
        {
            var (schema, typeMap) = BuildParquetSchema(colSchemas);
            await using var writer = new ParquetFileWriter(outputPath, schema, NullLogger.Instance, typeMap);
            foreach (var file in inputFiles)
                await foreach (var row in StreamRows(file, viewNav, schemaProvider, evaluator, vars, cancellationToken))
                { await writer.WriteRowAsync(row); rows++; }
            await writer.FlushAsync(cancellationToken);
        }
        else if (format == "csv")
        {
            await using var writer = new CsvFileWriter(outputPath, NullLogger.Instance);
            foreach (var file in inputFiles)
                await foreach (var row in StreamRows(file, viewNav, schemaProvider, evaluator, vars, cancellationToken))
                { await writer.WriteRowAsync(row, cancellationToken); rows++; }
            await writer.FlushAsync(cancellationToken);
        }
        else // ndjson
        {
            await using var writer = new NdjsonFileWriter(outputPath, NullLogger.Instance);
            foreach (var file in inputFiles)
                await foreach (var row in StreamRows(file, viewNav, schemaProvider, evaluator, vars, cancellationToken))
                { await writer.WriteRowAsync(row, cancellationToken); rows++; }
            await writer.FlushAsync(cancellationToken);
        }
        return rows;
    }

    private static async IAsyncEnumerable<Dictionary<string, object?>> StreamRows(
        string inputPath,
        ISourceNavigator viewNav,
        IFhirSchemaProvider schemaProvider,
        SqlOnFhirEvaluator evaluator,
        IReadOnlyDictionary<string, string> vars,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var line in File.ReadLinesAsync(inputPath, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var node = JsonSourceNodeFactory.Parse(line);
            if (node is null) continue;
            var element = node.ToElement(schemaProvider);
            var rows = evaluator.Evaluate(viewNav, element, vars);
            if (rows is null) continue;
            foreach (var row in rows) yield return row;
        }
    }

    private static ISourceNavigator? ParseViewDefinition(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSourceNodeFactory.Parse(json)?.ToSourceNavigator();
    }

    private static string? DetectFormat(string outputPath) =>
        Path.GetExtension(outputPath).ToUpperInvariant() switch
        {
            ".PARQUET" => "parquet",
            ".CSV"     => "csv",
            ".NDJSON"  => "ndjson",
            _          => null
        };

    private static void Print(bool quiet, string message) { if (!quiet) Console.WriteLine(message); }

    private static (ParquetSchema Schema, Dictionary<string, string> TypeMap) BuildParquetSchema(
        IReadOnlyList<ColumnSchema> columnSchemas)
    {
        var fields  = new List<DataField>();
        var typeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in columnSchemas)
        {
            var sqlType = (col.Type ?? "STRING").ToUpperInvariant();
            fields.Add(MapToParquetField(col.Name, sqlType));
            typeMap[col.Name] = sqlType;
        }
        if (fields.Count == 0) { fields.Add(new DataField<string>("id")); typeMap["id"] = "STRING"; }
        return (new ParquetSchema(fields), typeMap);
    }

    private static DataField MapToParquetField(string name, string sqlType) => sqlType switch
    {
        "BOOLEAN"  => new DataField<bool?>(name),
        "INTEGER"  => new DataField<int?>(name),
        "DECIMAL"  => new DataField<decimal?>(name),
        "DATE"     => new DataField<DateTime?>(name),
        "DATETIME" => new DataField<DateTimeOffset?>(name),
        _          => new DataField<string>(name)
    };
}
```

- [ ] **Step 2: Delete ConvertCommand**

```bash
git rm tools/Ignixa.SqlOnFhir.Cli/Commands/ConvertCommand.cs
```

- [ ] **Step 3: Build Writers to verify NdjsonFileWriter compiles cleanly**

```bash
dotnet build src/Core/Ignixa.SqlOnFhir.Writers/Ignixa.SqlOnFhir.Writers.csproj
```
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add tools/Ignixa.SqlOnFhir.Cli/Commands/RunCommand.cs
git commit -m "feat(sqlonfhir-cli): add RunCommand; remove ConvertCommand"
```

---

### Task 7: PreviewCommand — optional --input, --pattern, dir mode

**Files:**
- Modify: `tools/Ignixa.SqlOnFhir.Cli/Commands/PreviewCommand.cs`

Fully replace the file. `--input` becomes optional (`string?`). Dir mode iterates ViewDefinitions via `BatchProcessor.DiscoverViewDefinitions`. Schema-only mode runs when `--input` is omitted.

- [ ] **Step 1: Replace PreviewCommand.cs**

```csharp
// tools/Ignixa.SqlOnFhir.Cli/Commands/PreviewCommand.cs
using System.CommandLine;
using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.SqlOnFhir.Cli.Batch;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Parsing;

namespace Ignixa.SqlOnFhir.Cli.Commands;

internal static class PreviewCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider, string fhirVersion)
    {
        var cmd = new Command("preview", "Preview schema and sample rows. Omit --input for schema-only mode.");

        var viewsOpt   = new Option<string>("--views")   { Description = "ViewDefinition file or directory", Required = true };
        var inputOpt   = new Option<string?>("--input")  { Description = "NDJSON file or directory (optional; omit for schema-only)" };
        var rowsOpt    = new Option<int>("--rows")       { Description = "Max sample rows per ViewDefinition (default: 5)", DefaultValueFactory = _ => 5 };
        var patternOpt = new Option<string>("--pattern") { Description = "ViewDefinition glob, dir mode only (default: **/*.json)", DefaultValueFactory = _ => "**/*.json" };

        cmd.Options.Add(viewsOpt);
        cmd.Options.Add(inputOpt);
        cmd.Options.Add(rowsOpt);
        cmd.Options.Add(patternOpt);

        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var views   = parseResult.GetValue(viewsOpt)!;
            var input   = parseResult.GetValue(inputOpt);
            var rows    = parseResult.GetValue(rowsOpt);
            var pattern = parseResult.GetValue(patternOpt)!;

            if (Directory.Exists(views))
                await PreviewDir(schemaProvider, fhirVersion, views, input, rows, pattern);
            else
                await PreviewSingle(schemaProvider, fhirVersion, views, input, rows);
        });

        return cmd;
    }

    private static async Task PreviewSingle(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string viewsPath,
        string? inputPath,
        int maxRows)
    {
        if (!File.Exists(viewsPath)) { Console.WriteLine($"✗ ViewDefinition not found: {viewsPath}"); Environment.ExitCode = 1; return; }
        if (inputPath is not null && !File.Exists(inputPath)) { Console.WriteLine($"✗ Input not found: {inputPath}"); Environment.ExitCode = 1; return; }

        var viewNav = ParseViewDefinition(viewsPath);
        if (viewNav is null) { Console.WriteLine("✗ Failed to parse ViewDefinition"); Environment.ExitCode = 1; return; }

        PrintSchema(viewNav, fhirVersion);

        if (inputPath is not null)
            await PrintSampleRows(inputPath, viewNav, schemaProvider, maxRows);
    }

    private static async Task PreviewDir(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string viewsDir,
        string? inputDir,
        int maxRows,
        string pattern)
    {
        var viewFiles = BatchProcessor.DiscoverViewDefinitions(viewsDir, pattern).ToList();
        if (viewFiles.Count == 0) { Console.WriteLine($"✗ No ViewDefinition files found in {viewsDir}"); Environment.ExitCode = 1; return; }

        Console.WriteLine($"Found {viewFiles.Count} ViewDefinition(s)\n");

        foreach (var vdPath in viewFiles)
        {
            Console.WriteLine($"── {Path.GetFileName(vdPath)} ────────────────────────────────────");
            var viewNav = ParseViewDefinition(vdPath);
            if (viewNav is null) { Console.WriteLine("  ✗ Parse error\n"); continue; }

            PrintSchema(viewNav, fhirVersion);

            if (inputDir is not null && Directory.Exists(inputDir))
            {
                var resource   = viewNav.Children("resource").FirstOrDefault()?.Text ?? string.Empty;
                var inputFiles = BatchProcessor.FindInputFiles(inputDir, resource, "*{resource}*.ndjson").ToList();
                if (inputFiles.Count > 0)
                    await PrintSampleRows(inputFiles[0], viewNav, schemaProvider, maxRows);
                else
                    Console.WriteLine($"  (no matching NDJSON for '{resource}')");
            }

            Console.WriteLine();
        }
    }

    private static void PrintSchema(ISourceNavigator viewNav, string fhirVersion)
    {
        Console.WriteLine();
        Console.WriteLine("=== Schema ===");
        Console.WriteLine();

        try
        {
            var viewExpr   = ViewDefinitionExpressionParser.Parse(viewNav);
            var schemaEval = new SqlOnFhirSchemaEvaluator();
            var columns    = schemaEval.GetSchema(viewExpr);

            if (columns.Count > 0)
            {
                var maxLen = columns.Max(c => c.Name.Length);
                foreach (var col in columns.OrderBy(c => c.Name))
                    Console.WriteLine($"  {col.Name.PadRight(maxLen)}  {col.Type ?? "inferred"}{(col.Collection ? " (collection)" : "")}");
            }

            Console.WriteLine();
            Console.WriteLine($"Resource: {viewExpr.Resource}  |  FHIR: {fhirVersion.ToUpperInvariant()}  |  Columns: {columns.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Schema extraction failed: {ex.Message}");
        }
    }

    private static async Task PrintSampleRows(
        string inputPath,
        ISourceNavigator viewNav,
        IFhirSchemaProvider schemaProvider,
        int maxRows)
    {
        var evaluator  = new SqlOnFhirEvaluator();
        var sampleRows = new List<Dictionary<string, object?>>();
        var reachedMax = false;

        await foreach (var line in File.ReadLinesAsync(inputPath))
        {
            if (reachedMax) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var node = JsonSourceNodeFactory.Parse(line);
            if (node is null) continue;
            var element = node.ToElement(schemaProvider);
            var rows = evaluator.Evaluate(viewNav, element);
            if (rows is null) continue;
            foreach (var row in rows)
            {
                sampleRows.Add(row);
                if (sampleRows.Count >= maxRows) { reachedMax = true; break; }
            }
        }

        if (sampleRows.Count == 0) { Console.WriteLine("\n(no rows generated)"); return; }

        Console.WriteLine();
        Console.WriteLine($"=== Sample Rows ({sampleRows.Count}) ===");
        Console.WriteLine();

        var viewExpr   = ViewDefinitionExpressionParser.Parse(viewNav);
        var schemaEval = new SqlOnFhirSchemaEvaluator();
        var columns    = schemaEval.GetSchema(viewExpr).OrderBy(c => c.Name).Select(c => c.Name).ToList();
        DisplayTable(sampleRows, columns);
        Console.WriteLine($"\n✓ Preview completed with {sampleRows.Count} sample row(s)");
    }

    private static void DisplayTable(List<Dictionary<string, object?>> rows, List<string> columns)
    {
        var widths = columns.ToDictionary(c => c, c =>
        {
            var max = c.Length;
            foreach (var row in rows)
                if (row.TryGetValue(c, out var v) && v != null)
                    max = Math.Max(max, FormatValue(v).Length);
            return Math.Min(max, 50);
        });

        var header = string.Join(" | ", columns.Select(c => c.PadRight(widths[c])));
        Console.WriteLine(header);
        Console.WriteLine(new string('-', header.Length));
        foreach (var row in rows)
        {
            var cells = columns.Select(c =>
            {
                if (row.TryGetValue(c, out var v) && v != null)
                {
                    var s = FormatValue(v);
                    if (s.Length > widths[c]) s = string.Concat(s.AsSpan(0, widths[c] - 3), "...");
                    return s.PadRight(widths[c]);
                }
                return string.Empty.PadRight(widths[c]);
            });
            Console.WriteLine(string.Join(" | ", cells));
        }
    }

    private static string FormatValue(object value) => value switch
    {
        DateTime dt        => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        decimal d          => d.ToString("0.##", CultureInfo.InvariantCulture),
        double dbl         => dbl.ToString("0.##", CultureInfo.InvariantCulture),
        float f            => f.ToString("0.##", CultureInfo.InvariantCulture),
        bool b             => b ? "true" : "false",
        _                  => value.ToString() ?? string.Empty
    };

    private static ISourceNavigator? ParseViewDefinition(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSourceNodeFactory.Parse(json)?.ToSourceNavigator();
    }
}
```

- [ ] **Step 2: Build CLI (Program.cs still references old flag names — harmless until Task 9)**

```bash
dotnet build tools/Ignixa.SqlOnFhir.Cli/Ignixa.SqlOnFhir.Cli.csproj 2>&1 | grep " error " | head -10
```
Expected: 0 build errors.

- [ ] **Step 3: Commit**

```bash
git add tools/Ignixa.SqlOnFhir.Cli/Commands/PreviewCommand.cs
git commit -m "feat(sqlonfhir-cli): extend preview with dir mode and optional --input"
```

---

### Task 8: ValidateCommand — --pattern + dir mode + summary table

**Files:**
- Modify: `tools/Ignixa.SqlOnFhir.Cli/Commands/ValidateCommand.cs`

Fully replace the file. Single mode is unchanged in behaviour; dir mode prints a summary table and exits 1 if any fail or all are skipped.

- [ ] **Step 1: Replace ValidateCommand.cs**

```csharp
// tools/Ignixa.SqlOnFhir.Cli/Commands/ValidateCommand.cs
using System.CommandLine;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.SqlOnFhir.Cli.Batch;
using Ignixa.SqlOnFhir.Parsing;

namespace Ignixa.SqlOnFhir.Cli.Commands;

internal static class ValidateCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider, string fhirVersion)
    {
        var cmd = new Command("validate", "Validate ViewDefinition file(s).");

        var viewsOpt   = new Option<string>("--views")   { Description = "ViewDefinition file or directory", Required = true };
        var patternOpt = new Option<string>("--pattern") { Description = "ViewDefinition glob, dir mode only (default: **/*.json)", DefaultValueFactory = _ => "**/*.json" };

        cmd.Options.Add(viewsOpt);
        cmd.Options.Add(patternOpt);

        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var views   = parseResult.GetValue(viewsOpt)!;
            var pattern = parseResult.GetValue(patternOpt)!;

            if (Directory.Exists(views))
                await ValidateDir(views, pattern);
            else
                await ValidateSingle(views);
        });

        return cmd;
    }

    private static async Task ValidateSingle(string viewsPath)
    {
        if (!File.Exists(viewsPath)) { Console.WriteLine($"✗ ViewDefinition not found: {viewsPath}"); Environment.ExitCode = 1; return; }

        var (valid, message, info) = await Validate(viewsPath);
        if (valid)
        {
            Console.WriteLine($"✓ Valid JSON format");
            Console.WriteLine($"✓ Resource type is ViewDefinition");
            Console.WriteLine($"✓ ViewDefinition parsed successfully");
            Console.WriteLine($"  {info}");
            Console.WriteLine();
            Console.WriteLine("✓ ViewDefinition is valid");
        }
        else
        {
            Console.WriteLine($"✗ {message}");
            Environment.ExitCode = 1;
        }
    }

    private static async Task ValidateDir(string viewsDir, string pattern)
    {
        var viewFiles = BatchProcessor.DiscoverViewDefinitions(viewsDir, pattern).ToList();
        if (viewFiles.Count == 0) { Console.WriteLine($"✗ No ViewDefinition files found in {viewsDir}"); Environment.ExitCode = 1; return; }

        Console.WriteLine($"Validating {viewFiles.Count} ViewDefinition(s)\n");

        var results = new List<(string Name, bool Valid, string Detail)>();
        foreach (var vdPath in viewFiles)
        {
            var (valid, message, info) = await Validate(vdPath);
            results.Add((Path.GetFileName(vdPath), valid, valid ? info : message ?? string.Empty));
        }

        var nameWidth   = Math.Max(results.Max(r => r.Name.Length),   4);
        var detailWidth = Math.Max(results.Max(r => r.Detail.Length), 6);
        Console.WriteLine($"  {"Name".PadRight(nameWidth)}  Status  {"Detail".PadRight(detailWidth)}");
        Console.WriteLine($"  {new string('-', nameWidth)}  ------  {new string('-', detailWidth)}");
        foreach (var (name, valid, detail) in results)
            Console.WriteLine($"  {name.PadRight(nameWidth)}  {(valid ? "  ✓  " : "  ✗  ")}  {detail}");

        Console.WriteLine();
        var passed = results.Count(r => r.Valid);
        var failed = results.Count(r => !r.Valid);
        Console.WriteLine($"{(failed > 0 ? "✗" : "✓")} {passed} passed, {failed} failed");

        if (failed > 0 || passed == 0)
            Environment.ExitCode = 1;
    }

    private static async Task<(bool Valid, string? Message, string Info)> Validate(string vdPath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(vdPath);
            var node = JsonSourceNodeFactory.Parse(json);
            if (node is null) return (false, "Failed to parse JSON", string.Empty);

            var nav          = node.ToSourceNavigator();
            var resourceType = nav.Children("resourceType").FirstOrDefault()?.Text;
            if (resourceType != "ViewDefinition")
                return (false, $"Not a ViewDefinition (found: {resourceType ?? "null"})", string.Empty);

            var viewDef   = ViewDefinitionExpressionParser.Parse(nav);
            var totalCols = viewDef.Select.Sum(s => s.Columns.Length);
            return (true, null, $"resource={viewDef.Resource}  columns={totalCols}  selects={viewDef.Select.Length}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message, string.Empty);
        }
    }
}
```

- [ ] **Step 2: Build CLI**

```bash
dotnet build tools/Ignixa.SqlOnFhir.Cli/Ignixa.SqlOnFhir.Cli.csproj 2>&1 | grep " error " | head -10
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add tools/Ignixa.SqlOnFhir.Cli/Commands/ValidateCommand.cs
git commit -m "feat(sqlonfhir-cli): extend validate with dir mode and summary table"
```

---

### Task 9: Wire Program.cs

**Files:**
- Modify: `tools/Ignixa.SqlOnFhir.Cli/Program.cs`

Register `RunCommand`; remove the `convert` reference; wire up the updated `PreviewCommand` and `ValidateCommand`.

- [ ] **Step 1: Replace Program.cs**

```csharp
// tools/Ignixa.SqlOnFhir.Cli/Program.cs
using System.CommandLine;
using Ignixa.Abstractions;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Ignixa.SqlOnFhir.Cli.Commands;

namespace Ignixa.SqlOnFhir.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("SQL on FHIR - Process FHIR resources using ViewDefinitions");

        AddFhirVersionCommands(rootCommand, "stu3", new STU3CoreSchemaProvider());
        AddFhirVersionCommands(rootCommand, "r4",   new R4CoreSchemaProvider());
        AddFhirVersionCommands(rootCommand, "r4b",  new R4BCoreSchemaProvider());
        AddFhirVersionCommands(rootCommand, "r5",   new R5CoreSchemaProvider());
        AddFhirVersionCommands(rootCommand, "r6",   new R6CoreSchemaProvider());

        return await rootCommand.Parse(args).InvokeAsync();
    }

    private static void AddFhirVersionCommands(RootCommand root, string versionCode, IFhirSchemaProvider schemaProvider)
    {
        var command = new Command(versionCode, $"Use FHIR {versionCode.ToUpperInvariant()} specification");
        command.Subcommands.Add(RunCommand.Create(schemaProvider, versionCode));
        command.Subcommands.Add(PreviewCommand.Create(schemaProvider, versionCode));
        command.Subcommands.Add(ValidateCommand.Create(schemaProvider, versionCode));
        root.Subcommands.Add(command);
    }
}
```

- [ ] **Step 2: Full CLI build**

```bash
dotnet build tools/Ignixa.SqlOnFhir.Cli/Ignixa.SqlOnFhir.Cli.csproj
```
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 3: Smoke test**

```bash
dotnet run --project tools/Ignixa.SqlOnFhir.Cli/Ignixa.SqlOnFhir.Cli.csproj -- r4 --help
```
Expected: Output shows `run`, `preview`, `validate`. No `convert`.

- [ ] **Step 4: Commit**

```bash
git add tools/Ignixa.SqlOnFhir.Cli/Program.cs
git commit -m "feat(sqlonfhir-cli): wire run/preview/validate; drop convert"
```

---

### Task 10: Fixtures and integration tests

**Files:**
- Create: `test/Ignixa.SqlOnFhir.Cli.Tests/Fixtures/views/patient-demographics.json`
- Create: `test/Ignixa.SqlOnFhir.Cli.Tests/Fixtures/views/observation-codes.json`
- Create: `test/Ignixa.SqlOnFhir.Cli.Tests/Fixtures/fhir/Patient.ndjson`
- Create: `test/Ignixa.SqlOnFhir.Cli.Tests/Fixtures/fhir/Observation.ndjson`
- Create: `test/Ignixa.SqlOnFhir.Cli.Tests/Integration/RunCommandIntegrationTests.cs`
- Create: `test/Ignixa.SqlOnFhir.Cli.Tests/Integration/PreviewCommandIntegrationTests.cs`
- Create: `test/Ignixa.SqlOnFhir.Cli.Tests/Integration/ValidateCommandIntegrationTests.cs`

- [ ] **Step 1: Create fixture ViewDefinitions**

```json
// test/Ignixa.SqlOnFhir.Cli.Tests/Fixtures/views/patient-demographics.json
{
  "resourceType": "ViewDefinition",
  "resource": "Patient",
  "select": [{
    "column": [
      { "name": "id",     "path": "id",         "type": "id"     },
      { "name": "family", "path": "name.family", "type": "string" }
    ]
  }]
}
```

```json
// test/Ignixa.SqlOnFhir.Cli.Tests/Fixtures/views/observation-codes.json
{
  "resourceType": "ViewDefinition",
  "resource": "Observation",
  "select": [{
    "column": [
      { "name": "id",   "path": "id",                "type": "id"     },
      { "name": "code", "path": "code.coding.code",  "type": "string" }
    ]
  }]
}
```

- [ ] **Step 2: Create fixture NDJSON data**

`test/Ignixa.SqlOnFhir.Cli.Tests/Fixtures/fhir/Patient.ndjson` — two lines (one JSON object per line, no trailing newline):
```
{"resourceType":"Patient","id":"p1","name":[{"family":"Smith","given":["John"]}]}
{"resourceType":"Patient","id":"p2","name":[{"family":"Jones","given":["Jane"]}]}
```

`test/Ignixa.SqlOnFhir.Cli.Tests/Fixtures/fhir/Observation.ndjson` — two lines:
```
{"resourceType":"Observation","id":"o1","code":{"coding":[{"code":"8480-6","system":"http://loinc.org"}]}}
{"resourceType":"Observation","id":"o2","code":{"coding":[{"code":"8462-4","system":"http://loinc.org"}]}}
```

- [ ] **Step 3: Add fixture copy rule to .csproj**

Add inside `<Project>` in `test/Ignixa.SqlOnFhir.Cli.Tests/Ignixa.SqlOnFhir.Cli.Tests.csproj`:

```xml
  <ItemGroup>
    <None Update="Fixtures\**\*">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

- [ ] **Step 4: Write RunCommand integration tests**

```csharp
// test/Ignixa.SqlOnFhir.Cli.Tests/Integration/RunCommandIntegrationTests.cs
using Ignixa.Specification.Generated;
using Ignixa.SqlOnFhir.Cli.Commands;
using Shouldly;

namespace Ignixa.SqlOnFhir.Cli.Tests.Integration;

public class RunCommandIntegrationTests : IDisposable
{
    private readonly string _outDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private static readonly string Views = Path.Combine(AppContext.BaseDirectory, "Fixtures", "views");
    private static readonly string Fhir  = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fhir");

    public RunCommandIntegrationTests() => Directory.CreateDirectory(_outDir);

    [Fact]
    public async Task GivenSingleViewAndInput_WhenRunCsv_ThenCsvCreatedWithHeaderAndRows()
    {
        var vdPath  = Path.Combine(Views, "patient-demographics.json");
        var inPath  = Path.Combine(Fhir,  "Patient.ndjson");
        var outPath = Path.Combine(_outDir, "out.csv");
        var cmd     = RunCommand.Create(new R4CoreSchemaProvider(), "r4");

        await cmd.Parse(["--views", vdPath, "--input", inPath, "--out", outPath, "--quiet"]).InvokeAsync();

        File.Exists(outPath).ShouldBeTrue();
        var lines = (await File.ReadAllLinesAsync(outPath)).Where(l => l.Length > 0).ToArray();
        lines.Length.ShouldBeGreaterThanOrEqualTo(2);
        lines[0].ShouldContain("id");
        lines[0].ShouldContain("family");
    }

    [Fact]
    public async Task GivenSingleViewAndInput_WhenRunNdjson_ThenNdjsonCreatedWithValidJsonLines()
    {
        var vdPath  = Path.Combine(Views, "patient-demographics.json");
        var inPath  = Path.Combine(Fhir,  "Patient.ndjson");
        var outPath = Path.Combine(_outDir, "out.ndjson");
        var cmd     = RunCommand.Create(new R4CoreSchemaProvider(), "r4");

        await cmd.Parse(["--views", vdPath, "--input", inPath, "--out", outPath, "--quiet"]).InvokeAsync();

        File.Exists(outPath).ShouldBeTrue();
        var lines = (await File.ReadAllLinesAsync(outPath)).Where(l => l.Length > 0).ToArray();
        lines.Length.ShouldBeGreaterThanOrEqualTo(1);
        foreach (var line in lines)
            Should.NotThrow(() => System.Text.Json.JsonDocument.Parse(line));
    }

    [Fact]
    public async Task GivenViewsAndInputDirs_WhenRunBatch_ThenOneOutputFilePerViewDef()
    {
        var cmd = RunCommand.Create(new R4CoreSchemaProvider(), "r4");

        await cmd.Parse(["--views", Views, "--input", Fhir, "--out", _outDir, "--format", "csv", "--quiet"]).InvokeAsync();

        File.Exists(Path.Combine(_outDir, "patient-demographics.csv")).ShouldBeTrue();
        File.Exists(Path.Combine(_outDir, "observation-codes.csv")).ShouldBeTrue();
    }

    [Fact]
    public async Task GivenBatchWithNoMatchingNdjson_WhenRun_ThenAllSkippedExitCode1()
    {
        var viewsDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(viewsDir);
        await File.WriteAllTextAsync(Path.Combine(viewsDir, "allergy.json"),
            """{"resourceType":"ViewDefinition","resource":"AllergyIntolerance","select":[{"column":[{"name":"id","path":"id"}]}]}""");

        var cmd = RunCommand.Create(new R4CoreSchemaProvider(), "r4");
        Environment.ExitCode = 0;

        await cmd.Parse(["--views", viewsDir, "--input", Fhir, "--out", _outDir, "--quiet"]).InvokeAsync();

        Environment.ExitCode.ShouldBe(1);
        Directory.Delete(viewsDir, recursive: true);
    }

    [Fact]
    public async Task GivenBatchRun_WhenStatsOutProvided_ThenStatsJsonWritten()
    {
        var statsPath = Path.Combine(_outDir, "stats.json");
        var cmd       = RunCommand.Create(new R4CoreSchemaProvider(), "r4");

        await cmd.Parse(["--views", Views, "--input", Fhir, "--out", _outDir,
                         "--format", "csv", "--quiet", "--stats-out", statsPath]).InvokeAsync();

        File.Exists(statsPath).ShouldBeTrue();
        var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(statsPath));
        json.RootElement.GetProperty("total").GetInt32().ShouldBeGreaterThan(0);
        json.RootElement.GetProperty("completed").GetInt32().ShouldBeGreaterThan(0);
    }

    public void Dispose() => Directory.Delete(_outDir, recursive: true);
}
```

- [ ] **Step 5: Write PreviewCommand integration tests**

```csharp
// test/Ignixa.SqlOnFhir.Cli.Tests/Integration/PreviewCommandIntegrationTests.cs
using Ignixa.Specification.Generated;
using Ignixa.SqlOnFhir.Cli.Commands;
using Shouldly;

namespace Ignixa.SqlOnFhir.Cli.Tests.Integration;

public class PreviewCommandIntegrationTests
{
    private static readonly string Views = Path.Combine(AppContext.BaseDirectory, "Fixtures", "views");
    private static readonly string Fhir  = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fhir");

    [Fact]
    public async Task GivenViewFileWithNoInput_WhenPreview_ThenExitsZero()
    {
        var vdPath = Path.Combine(Views, "patient-demographics.json");
        var cmd    = PreviewCommand.Create(new R4CoreSchemaProvider(), "r4");
        Environment.ExitCode = 0;

        await cmd.Parse(["--views", vdPath]).InvokeAsync();

        Environment.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task GivenViewFileWithInput_WhenPreview_ThenExitsZero()
    {
        var vdPath = Path.Combine(Views, "patient-demographics.json");
        var inPath = Path.Combine(Fhir,  "Patient.ndjson");
        var cmd    = PreviewCommand.Create(new R4CoreSchemaProvider(), "r4");
        Environment.ExitCode = 0;

        await cmd.Parse(["--views", vdPath, "--input", inPath, "--rows", "2"]).InvokeAsync();

        Environment.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task GivenViewsDir_WhenPreviewDir_ThenExitsZero()
    {
        var cmd = PreviewCommand.Create(new R4CoreSchemaProvider(), "r4");
        Environment.ExitCode = 0;

        await cmd.Parse(["--views", Views]).InvokeAsync();

        Environment.ExitCode.ShouldBe(0);
    }
}
```

- [ ] **Step 6: Write ValidateCommand integration tests**

```csharp
// test/Ignixa.SqlOnFhir.Cli.Tests/Integration/ValidateCommandIntegrationTests.cs
using Ignixa.Specification.Generated;
using Ignixa.SqlOnFhir.Cli.Commands;
using Shouldly;

namespace Ignixa.SqlOnFhir.Cli.Tests.Integration;

public class ValidateCommandIntegrationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private static readonly string Views = Path.Combine(AppContext.BaseDirectory, "Fixtures", "views");

    public ValidateCommandIntegrationTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public async Task GivenValidViewDefinition_WhenValidateSingle_ThenExitsZero()
    {
        var vdPath = Path.Combine(Views, "patient-demographics.json");
        var cmd    = ValidateCommand.Create(new R4CoreSchemaProvider(), "r4");
        Environment.ExitCode = 0;

        await cmd.Parse(["--views", vdPath]).InvokeAsync();

        Environment.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task GivenNonViewDefinitionJson_WhenValidateSingle_ThenExitsOne()
    {
        var badPath = Path.Combine(_tempDir, "bad.json");
        await File.WriteAllTextAsync(badPath, """{"resourceType":"Patient","id":"p1"}""");
        var cmd = ValidateCommand.Create(new R4CoreSchemaProvider(), "r4");
        Environment.ExitCode = 0;

        await cmd.Parse(["--views", badPath]).InvokeAsync();

        Environment.ExitCode.ShouldBe(1);
    }

    [Fact]
    public async Task GivenValidViewsDir_WhenValidateDir_ThenExitsZero()
    {
        var cmd = ValidateCommand.Create(new R4CoreSchemaProvider(), "r4");
        Environment.ExitCode = 0;

        await cmd.Parse(["--views", Views]).InvokeAsync();

        Environment.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task GivenMixedDir_WhenValidateDir_ThenExitsOne()
    {
        File.Copy(Path.Combine(Views, "patient-demographics.json"), Path.Combine(_tempDir, "valid.json"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "invalid.json"), """{"resourceType":"Patient"}""");
        var cmd = ValidateCommand.Create(new R4CoreSchemaProvider(), "r4");
        Environment.ExitCode = 0;

        await cmd.Parse(["--views", _tempDir]).InvokeAsync();

        Environment.ExitCode.ShouldBe(1);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);
}
```

- [ ] **Step 7: Run all tests in the new project**

```bash
dotnet test test/Ignixa.SqlOnFhir.Cli.Tests/Ignixa.SqlOnFhir.Cli.Tests.csproj -v minimal
```
Expected: All tests pass.

- [ ] **Step 8: Commit**

```bash
git add test/Ignixa.SqlOnFhir.Cli.Tests/
git commit -m "test(sqlonfhir-cli): add fixtures and integration tests"
```

---

### Task 11: Full build and verification

- [ ] **Step 1: Build the entire solution**

```bash
dotnet build All.sln
```
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 2: Run all tests**

```bash
dotnet test All.sln -v minimal
```
Expected: All tests pass.

- [ ] **Step 3: Final commit if any stray changes**

```bash
git status
# If clean, nothing to do. If any changes:
git add -A
git commit -m "chore(sqlonfhir-cli): final build verification"
```

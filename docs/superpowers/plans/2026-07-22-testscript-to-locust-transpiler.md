# TestScript-to-Locust Transpiler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Compile every scenario supported by the current Ignixa TestScript evaluator into a flat, Azure Load Testing-compatible Locust artifact with equivalent single-execution behavior.

**Architecture:** A new packable `Ignixa.TestScript.Locust` core library lowers the existing typed TestScript model into a versioned semantic JSON IR and copies a shared Python runtime into the artifact. The runtime executes one isolated setup-test-teardown cycle per Locust task iteration, sends all HTTP through Locust, and evaluates compatible FHIRPath expressions with `fhirpathpy`.

**Tech Stack:** .NET 9/10, C#, System.Text.Json, Ignixa.TestScript, Ignixa.FhirPath, Ignixa.FhirFakes, System.CommandLine, Python 3.9.19, Locust 2.33.2, fhirpathpy 2.1.0, xUnit, Shouldly, Python unittest.

---

## Scope and delivery order

The compiler, runtime, and compatibility tests are coupled: the compiler is not useful without a
runtime that accepts its IR, and the runtime cannot claim parity without the .NET contract tests.
Keep them in one plan, delivered as vertical slices:

1. Define and serialize the IR.
2. Analyze and lower TestScript semantics.
3. Generate a flat artifact and expose it through the CLI.
4. Execute lifecycle, operations, variables, and fixtures in Python.
5. Execute assertions, capability gates, and FHIRPath in Python.
6. Prove cross-language behavior and Azure engine compatibility.

## File structure

### New .NET compiler project

- `src/Core/Ignixa.TestScript.Locust/Ignixa.TestScript.Locust.csproj` — packable compiler library and embedded Python assets.
- `src/Core/Ignixa.TestScript.Locust/Ir/LocustIrDocument.cs` — versioned root document.
- `src/Core/Ignixa.TestScript.Locust/Ir/LocustIrMetadata.cs` — source identity and FHIR version.
- `src/Core/Ignixa.TestScript.Locust/Ir/LocustIrFixture.cs` — inline fixture variant pool and lifecycle flags.
- `src/Core/Ignixa.TestScript.Locust/Ir/LocustIrVariable.cs` — variable definition and extraction.
- `src/Core/Ignixa.TestScript.Locust/Ir/LocustIrVariableExtractionKind.cs` — closed extraction discriminator.
- `src/Core/Ignixa.TestScript.Locust/Ir/LocustIrTest.cs` — expanded test execution.
- `src/Core/Ignixa.TestScript.Locust/Ir/LocustIrAction.cs` — polymorphic action root.
- `src/Core/Ignixa.TestScript.Locust/Ir/LocustIrOperation.cs` — HTTP operation.
- `src/Core/Ignixa.TestScript.Locust/Ir/LocustIrAssertion.cs` — assertion and extension metadata.
- `src/Core/Ignixa.TestScript.Locust/Ir/LocustIrAssertionCriteria.cs` — assertion values.
- `src/Core/Ignixa.TestScript.Locust/Ir/LocustIrAssertionKind.cs` — closed assertion discriminator.
- `src/Core/Ignixa.TestScript.Locust/Ir/LocustIrHeader.cs` — request header template.
- `src/Core/Ignixa.TestScript.Locust/Ir/LocustIrWaitFor.cs` — polling condition.
- `src/Core/Ignixa.TestScript.Locust/Ir/LocustIrSerializer.cs` — canonical JSON options and schema version.
- `src/Core/Ignixa.TestScript.Locust/Diagnostics/LocustDiagnostic.cs` — source-qualified compiler diagnostic.
- `src/Core/Ignixa.TestScript.Locust/Diagnostics/LocustDiagnosticSeverity.cs` — info/warning/error discriminator.
- `src/Core/Ignixa.TestScript.Locust/Compilation/LocustCompilerOptions.cs` — source, version, schema, and fixture-pool inputs.
- `src/Core/Ignixa.TestScript.Locust/Compilation/LocustCompilationResult.cs` — document plus diagnostics.
- `src/Core/Ignixa.TestScript.Locust/Compilation/LocustSupportAnalyzer.cs` — rejects unsupported model nodes.
- `src/Core/Ignixa.TestScript.Locust/Compilation/LocustIrCompiler.cs` — lowers the typed model into IR.
- `src/Core/Ignixa.TestScript.Locust/Compilation/LocustFixtureCompiler.cs` — inline and generated fixture variants.
- `src/Core/Ignixa.TestScript.Locust/Compatibility/FhirPathCompatibilityManifest.cs` — loads known runtime incompatibilities.
- `src/Core/Ignixa.TestScript.Locust/Compatibility/FhirPathIncompatibility.cs` — one expression/usage incompatibility.
- `src/Core/Ignixa.TestScript.Locust/Compatibility/FhirPathUsage.cs` — boolean/scalar evaluation discriminator.
- `src/Core/Ignixa.TestScript.Locust/Compatibility/fhirpath-incompatibilities.json` — reviewed compatibility denylist.
- `src/Core/Ignixa.TestScript.Locust/Artifacts/LocustArtifactWriter.cs` — writes the flat artifact atomically.
- `src/Core/Ignixa.TestScript.Locust/Python/locustfile.py` — fixed Locust loader copied into artifacts.
- `src/Core/Ignixa.TestScript.Locust/Python/ignixa_testscript_runtime.py` — shared Python interpreter.
- `src/Core/Ignixa.TestScript.Locust/Python/requirements.txt` — Azure-compatible pinned dependencies.

### New tests

- `test/Ignixa.TestScript.Locust.Tests/Ignixa.TestScript.Locust.Tests.csproj` — compiler test project.
- `test/Ignixa.TestScript.Locust.Tests/Ir/LocustIrSerializerTests.cs` — schema and round-trip tests.
- `test/Ignixa.TestScript.Locust.Tests/Compilation/LocustSupportAnalyzerTests.cs` — support-matrix tests.
- `test/Ignixa.TestScript.Locust.Tests/Compilation/LocustIrCompilerTests.cs` — lowering tests.
- `test/Ignixa.TestScript.Locust.Tests/Compilation/LocustFixtureCompilerTests.cs` — fixture-pool tests.
- `test/Ignixa.TestScript.Locust.Tests/Artifacts/LocustArtifactWriterTests.cs` — flat artifact tests.
- `test/Ignixa.TestScript.Locust.Tests/Contracts/fhirpath-cases.json` — shared FHIRPath expectations.
- `test/Ignixa.TestScript.Locust.Tests/Contracts/runtime-cases.json` — shared request/outcome expectations.
- `test/Ignixa.TestScript.Locust.Tests/Contracts/FhirPathContractTests.cs` — Ignixa side of FHIRPath contracts.
- `test/Ignixa.TestScript.Locust.Tests/Python/test_runtime_lifecycle.py` — lifecycle and isolation tests.
- `test/Ignixa.TestScript.Locust.Tests/Python/test_runtime_operations.py` — operations and variables.
- `test/Ignixa.TestScript.Locust.Tests/Python/test_runtime_assertions.py` — assertions and event mapping.
- `test/Ignixa.TestScript.Locust.Tests/Python/test_fhirpath_contract.py` — fhirpathpy side of contracts.
- `test/Ignixa.TestScript.Locust.Tests/Python/fakes.py` — fake Locust user, client, response, and events.

### Existing files to modify

- `All.sln` — add the compiler and test projects.
- `tools/Ignixa.ConformanceMatrix.Cli/Ignixa.ConformanceMatrix.Cli.csproj` — reference the compiler.
- `tools/Ignixa.ConformanceMatrix.Cli/Program.cs` — register `compile-locust`.
- `tools/Ignixa.ConformanceMatrix.Cli/Commands/CompileLocustCommand.cs` — new command.
- `test/Ignixa.ConformanceMatrix.Cli.Tests/CompileLocustCommandTests.cs` — new command tests.
- `.github/workflows/pr-build.yml` — run Python 3.9 runtime tests.
- `.github/workflows/ci.yml` — run Python 3.9 runtime tests.
- `docs/site/docs/core-sdk/testscript.md` — document compilation and parity boundary.
- `docs/features/testscript/investigations/azure-load-testing.md` — link implementation and record the Python compatibility pin.

## Task 1: Scaffold the compiler and define the versioned IR

**Files:**
- Create: `src/Core/Ignixa.TestScript.Locust/Ignixa.TestScript.Locust.csproj`
- Create: `src/Core/Ignixa.TestScript.Locust/Ir/*.cs`
- Create: `test/Ignixa.TestScript.Locust.Tests/Ignixa.TestScript.Locust.Tests.csproj`
- Create: `test/Ignixa.TestScript.Locust.Tests/Ir/LocustIrSerializerTests.cs`
- Modify: `All.sln`

- [ ] **Step 1: Scaffold projects and add them to the solution**

Run:

```powershell
dotnet new classlib -n Ignixa.TestScript.Locust -o src\Core\Ignixa.TestScript.Locust
dotnet new xunit -n Ignixa.TestScript.Locust.Tests -o test\Ignixa.TestScript.Locust.Tests
dotnet sln All.sln add src\Core\Ignixa.TestScript.Locust\Ignixa.TestScript.Locust.csproj
dotnet sln All.sln add test\Ignixa.TestScript.Locust.Tests\Ignixa.TestScript.Locust.Tests.csproj
Remove-Item src\Core\Ignixa.TestScript.Locust\Class1.cs
Remove-Item test\Ignixa.TestScript.Locust.Tests\UnitTest1.cs
```

Expected: both projects appear in `dotnet sln All.sln list`.

- [ ] **Step 2: Configure the project references**

Replace the compiler project with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>true</IsPackable>
    <PackageStability>beta</PackageStability>
    <PackageId>Ignixa.TestScript.Locust</PackageId>
    <Description>Compile Ignixa FHIR TestScript definitions into Locust workloads.</Description>
    <PackageTags>fhir;testscript;locust;load-testing;azure</PackageTags>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Ignixa.TestScript.Locust.Tests" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Ignixa.FhirPath\Ignixa.FhirPath.csproj" />
    <ProjectReference Include="..\Ignixa.Serialization\Ignixa.Serialization.csproj" />
    <ProjectReference Include="..\Ignixa.Specification\Ignixa.Specification.csproj" />
    <ProjectReference Include="..\Ignixa.TestScript\Ignixa.TestScript.csproj" />
    <ProjectReference Include="..\Ignixa.TestScript.FhirFakes\Ignixa.TestScript.FhirFakes.csproj" />
  </ItemGroup>
  <ItemGroup>
    <EmbeddedResource Include="Python\*" />
  </ItemGroup>
</Project>
```

Replace the test project with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Shouldly" />
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Core\Ignixa.TestScript.Locust\Ignixa.TestScript.Locust.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Update="Contracts\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write the failing serializer test**

Create `LocustIrSerializerTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Ignixa.TestScript.Locust.Ir;

namespace Ignixa.TestScript.Locust.Tests.Ir;

public class LocustIrSerializerTests
{
    [Fact]
    public void GivenDocument_WhenSerialized_ThenUsesVersionedCamelCaseDiscriminatedShape()
    {
        var document = new LocustIrDocument
        {
            Metadata = new LocustIrMetadata("CRUD basic", "CRUD/basic.json", "4.0"),
            Setup =
            [
                new LocustIrOperation
                {
                    Id = "setup.0",
                    Type = "create",
                    Method = "POST",
                    Resource = "Patient"
                }
            ]
        };

        JsonNode json = JsonNode.Parse(LocustIrSerializer.Serialize(document))!;

        json["schemaVersion"]!.GetValue<string>().ShouldBe("1.0");
        json["metadata"]!["source"]!.GetValue<string>().ShouldBe("CRUD/basic.json");
        json["setup"]![0]!["kind"]!.GetValue<string>().ShouldBe("operation");
        json["setup"]![0]!["method"]!.GetValue<string>().ShouldBe("POST");
    }
}
```

- [ ] **Step 4: Run the test and verify it fails**

Run:

```powershell
dotnet test test\Ignixa.TestScript.Locust.Tests\Ignixa.TestScript.Locust.Tests.csproj --filter FullyQualifiedName~LocustIrSerializerTests
```

Expected: compilation fails because the IR types do not exist.

- [ ] **Step 5: Add the IR types**

Create one type per file with these definitions:

```csharp
// Ir/LocustIrDocument.cs
namespace Ignixa.TestScript.Locust.Ir;
public sealed record LocustIrDocument
{
    public string SchemaVersion { get; init; } = LocustIrSerializer.SchemaVersion;
    public string CompilerVersion { get; init; } = "0.1.0";
    public required LocustIrMetadata Metadata { get; init; }
    public string? RequiresCapability { get; init; }
    public IReadOnlyList<LocustIrFixture> Fixtures { get; init; } = [];
    public IReadOnlyList<LocustIrVariable> Variables { get; init; } = [];
    public IReadOnlyList<LocustIrAction> Setup { get; init; } = [];
    public IReadOnlyList<LocustIrTest> Tests { get; init; } = [];
    public IReadOnlyList<LocustIrOperation> Teardown { get; init; } = [];
}

// Ir/LocustIrMetadata.cs
namespace Ignixa.TestScript.Locust.Ir;
public sealed record LocustIrMetadata(string Name, string Source, string? FhirVersion);

// Ir/LocustIrFixture.cs
using System.Text.Json.Nodes;
namespace Ignixa.TestScript.Locust.Ir;
public sealed record LocustIrFixture(
    string Id,
    bool Autocreate,
    bool Autodelete,
    IReadOnlyList<JsonObject> Variants);

// Ir/LocustIrVariableExtractionKind.cs
namespace Ignixa.TestScript.Locust.Ir;
public enum LocustIrVariableExtractionKind { None, Header, Path, FhirPath }

// Ir/LocustIrVariable.cs
namespace Ignixa.TestScript.Locust.Ir;
public sealed record LocustIrVariable(
    string Name,
    string? DefaultValue,
    string? SourceId,
    LocustIrVariableExtractionKind ExtractionKind,
    string? Selector);

// Ir/LocustIrTest.cs
namespace Ignixa.TestScript.Locust.Ir;
public sealed record LocustIrTest
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? RequiresCapability { get; init; }
    public bool DiscardContextAfterExecution { get; init; }
    public IReadOnlyDictionary<string, string> InitialVariables { get; init; }
        = new Dictionary<string, string>();
    public IReadOnlyList<LocustIrAction> Actions { get; init; } = [];
}

// Ir/LocustIrAction.cs
using System.Text.Json.Serialization;
namespace Ignixa.TestScript.Locust.Ir;
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LocustIrOperation), "operation")]
[JsonDerivedType(typeof(LocustIrAssertion), "assert")]
public abstract record LocustIrAction
{
    public required string Id { get; init; }
    public string? Label { get; init; }
    public string? Description { get; init; }
}

// Ir/LocustIrHeader.cs
namespace Ignixa.TestScript.Locust.Ir;
public sealed record LocustIrHeader(string Field, string Value);

// Ir/LocustIrWaitFor.cs
namespace Ignixa.TestScript.Locust.Ir;
public sealed record LocustIrWaitFor(int PollingStatusCode, int MaxAttempts, int IntervalMs);

// Ir/LocustIrOperation.cs
namespace Ignixa.TestScript.Locust.Ir;
public sealed record LocustIrOperation : LocustIrAction
{
    public required string Type { get; init; }
    public required string Method { get; init; }
    public string? Resource { get; init; }
    public string? Url { get; init; }
    public string? Params { get; init; }
    public string? Accept { get; init; }
    public string? ContentType { get; init; }
    public string? SourceId { get; init; }
    public string? ResponseId { get; init; }
    public string? RequestId { get; init; }
    public bool EncodeRequestUrl { get; init; } = true;
    public IReadOnlyList<LocustIrHeader> Headers { get; init; } = [];
    public LocustIrWaitFor? WaitFor { get; init; }
}

// Ir/LocustIrAssertionKind.cs
namespace Ignixa.TestScript.Locust.Ir;
public enum LocustIrAssertionKind
{
    ResponseStatus, ResponseCode, ContentType, ResourceType, Header,
    FhirPath, FhirPathValue, RequestMethod, RequestUrl
}

// Ir/LocustIrAssertionCriteria.cs
namespace Ignixa.TestScript.Locust.Ir;
public sealed record LocustIrAssertionCriteria
{
    public required LocustIrAssertionKind Kind { get; init; }
    public string? Field { get; init; }
    public string? Expression { get; init; }
    public string? Value { get; init; }
    public string? Operator { get; init; }
}

// Ir/LocustIrAssertion.cs
namespace Ignixa.TestScript.Locust.Ir;
public sealed record LocustIrAssertion : LocustIrAction
{
    public required LocustIrAssertionCriteria Criteria { get; init; }
    public bool WarningOnly { get; init; }
    public string Direction { get; init; } = "response";
    public string? SourceId { get; init; }
    public string? AnyOfGroupId { get; init; }
    public string? WhenResponseSourceId { get; init; }
    public IReadOnlyList<int> WhenResponseStatuses { get; init; } = [];
}
```

Create `LocustIrSerializer.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ignixa.TestScript.Locust.Ir;

public static class LocustIrSerializer
{
    public const string SchemaVersion = "1.0";

    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize(LocustIrDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, s_options);
    }
}
```

- [ ] **Step 6: Run the focused test**

Run:

```powershell
dotnet test test\Ignixa.TestScript.Locust.Tests\Ignixa.TestScript.Locust.Tests.csproj --filter FullyQualifiedName~LocustIrSerializerTests
```

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add All.sln src\Core\Ignixa.TestScript.Locust test\Ignixa.TestScript.Locust.Tests
git commit -m "Add TestScript Locust intermediate representation"
```

## Task 2: Add explicit support analysis

**Files:**
- Create: `src/Core/Ignixa.TestScript.Locust/Diagnostics/LocustDiagnosticSeverity.cs`
- Create: `src/Core/Ignixa.TestScript.Locust/Diagnostics/LocustDiagnostic.cs`
- Create: `src/Core/Ignixa.TestScript.Locust/Compilation/LocustSupportAnalyzer.cs`
- Create: `test/Ignixa.TestScript.Locust.Tests/Compilation/LocustSupportAnalyzerTests.cs`

- [ ] **Step 1: Write failing analyzer tests**

Cover one accepted definition, multi-destination, `targetId`, `origin`, profiles, and
`encodeRequestUrl=false`:

```csharp
using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Locust.Compilation;
using Ignixa.TestScript.Locust.Diagnostics;
using Ignixa.TestScript.Model;

namespace Ignixa.TestScript.Locust.Tests.Compilation;

public class LocustSupportAnalyzerTests
{
    [Fact]
    public void GivenSupportedSingleDestinationDefinition_WhenAnalyzed_ThenHasNoErrors()
    {
        var definition = Build(new OperationExpression { Type = "read", Resource = "Patient" });

        var diagnostics = LocustSupportAnalyzer.Analyze(definition, "basic.json");

        diagnostics.ShouldNotContain(d => d.Severity == LocustDiagnosticSeverity.Error);
    }

    [Fact]
    public void GivenUnsupportedOperationFields_WhenAnalyzed_ThenReportsSourceQualifiedErrors()
    {
        var definition = Build(new OperationExpression
        {
            Type = "update",
            Destination = 2,
            Origin = 1,
            TargetId = "target"
        });

        var diagnostics = LocustSupportAnalyzer.Analyze(definition, "unsupported.json");

        diagnostics.Count(d => d.Severity == LocustDiagnosticSeverity.Error).ShouldBe(3);
        diagnostics.Where(d => d.Severity == LocustDiagnosticSeverity.Error)
            .ShouldAllBe(d => d.Source.StartsWith("unsupported.json:test:case:action:0"));
    }

    [Fact]
    public void GivenIgnoredEvaluatorFeatures_WhenAnalyzed_ThenReportsWarnings()
    {
        var definition = Build(new OperationExpression
        {
            Type = "read",
            EncodeRequestUrl = false
        }) with
        {
            Profiles =
            [
                new ProfileReference
                {
                    Id = "profile",
                    Canonical = "http://example.test/StructureDefinition/patient"
                }
            ]
        };

        var diagnostics = LocustSupportAnalyzer.Analyze(definition, "warning.json");

        diagnostics.ShouldContain(d => d.Code == "LOCUST004");
        diagnostics.ShouldContain(d => d.Code == "LOCUST005");
    }

    private static TestScriptDefinition Build(ActionExpression action) => new()
    {
        Metadata = new TestScriptMetadata { Name = "Suite" },
        Tests = [new TestPhaseDefinition { Name = "case", Actions = [action] }]
    };
}
```

- [ ] **Step 2: Run the test and verify it fails**

Run:

```powershell
dotnet test test\Ignixa.TestScript.Locust.Tests\Ignixa.TestScript.Locust.Tests.csproj --filter FullyQualifiedName~LocustSupportAnalyzerTests
```

Expected: compilation fails because analyzer types do not exist.

- [ ] **Step 3: Implement diagnostics and analyzer**

```csharp
// Diagnostics/LocustDiagnosticSeverity.cs
namespace Ignixa.TestScript.Locust.Diagnostics;
public enum LocustDiagnosticSeverity { Info, Warning, Error }

// Diagnostics/LocustDiagnostic.cs
namespace Ignixa.TestScript.Locust.Diagnostics;
public sealed record LocustDiagnostic(
    string Code,
    LocustDiagnosticSeverity Severity,
    string Source,
    string Message);
```

Implement `LocustSupportAnalyzer` by walking setup, tests, and teardown. Emit:

| Code | Severity | Condition |
|---|---|---|
| `LOCUST001` | Error | `Destination > 1` |
| `LOCUST002` | Error | non-null `Origin` |
| `LOCUST003` | Error | non-null `TargetId` |
| `LOCUST004` | Warning | `EncodeRequestUrl == false`; runtime preserves Ignixa's encode-and-warn behavior |
| `LOCUST005` | Warning | `Profiles.Count > 0`; evaluator does not consume profiles |
| `LOCUST006` | Error | an unknown `ActionExpression` or `AssertCriteria` subtype |

`Info` is reserved for emitted metric mappings. The support analyzer itself emits only warnings and
errors.

Use this complete traversal shape:

```csharp
using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Locust.Diagnostics;
using Ignixa.TestScript.Model;

namespace Ignixa.TestScript.Locust.Compilation;

public static class LocustSupportAnalyzer
{
    public static IReadOnlyList<LocustDiagnostic> Analyze(
        TestScriptDefinition definition,
        string source)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var diagnostics = new List<LocustDiagnostic>();
        if (definition.Profiles.Count > 0)
            diagnostics.Add(new("LOCUST005", LocustDiagnosticSeverity.Warning, source,
                "Profiles are parsed but are not evaluated by Ignixa.TestScript."));

        AnalyzeActions(definition.Setup, $"{source}:setup", diagnostics);
        foreach (var test in definition.Tests)
            AnalyzeActions(test.Actions, $"{source}:test:{test.Name}", diagnostics);
        AnalyzeActions(definition.Teardown, $"{source}:teardown", diagnostics);
        return diagnostics;
    }

    private static void AnalyzeActions(
        IReadOnlyList<ActionExpression> actions,
        string source,
        List<LocustDiagnostic> diagnostics)
    {
        for (var index = 0; index < actions.Count; index++)
        {
            var actionSource = $"{source}:action:{index}";
            switch (actions[index])
            {
                case OperationExpression operation:
                    AnalyzeOperation(operation, actionSource, diagnostics);
                    break;
                case AssertExpression assertion:
                    AnalyzeAssertion(assertion, actionSource, diagnostics);
                    break;
                default:
                    diagnostics.Add(new("LOCUST006", LocustDiagnosticSeverity.Error, actionSource,
                        $"Unsupported action type '{actions[index].GetType().Name}'."));
                    break;
            }
        }
    }

    private static void AnalyzeOperation(
        OperationExpression operation,
        string source,
        List<LocustDiagnostic> diagnostics)
    {
        if (operation.Destination is > 1)
            diagnostics.Add(new("LOCUST001", LocustDiagnosticSeverity.Error, source,
                "Only destination 1 is supported."));
        if (operation.Origin is not null)
            diagnostics.Add(new("LOCUST002", LocustDiagnosticSeverity.Error, source,
                "Origin execution is not supported."));
        if (operation.TargetId is not null)
            diagnostics.Add(new("LOCUST003", LocustDiagnosticSeverity.Error, source,
                "targetId is parsed but is not implemented by Ignixa.TestScript."));
        if (!operation.EncodeRequestUrl)
            diagnostics.Add(new("LOCUST004", LocustDiagnosticSeverity.Warning, source,
                "encodeRequestUrl=false is not implemented; URLs remain encoded."));
    }

    private static void AnalyzeAssertion(
        AssertExpression assertion,
        string source,
        List<LocustDiagnostic> diagnostics)
    {
        if (assertion.Criteria is not (
            ResponseStatusCriteria or ResponseCodeCriteria or ContentTypeCriteria
            or ResourceTypeCriteria or HeaderCriteria or FhirPathCriteria
            or FhirPathValueCriteria or RequestMethodCriteria or RequestUrlCriteria))
            diagnostics.Add(new("LOCUST006", LocustDiagnosticSeverity.Error, source,
                $"Unsupported assertion criteria '{assertion.Criteria.GetType().Name}'."));
    }
}
```

- [ ] **Step 4: Run analyzer tests**

Run:

```powershell
dotnet test test\Ignixa.TestScript.Locust.Tests\Ignixa.TestScript.Locust.Tests.csproj --filter FullyQualifiedName~LocustSupportAnalyzerTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.TestScript.Locust\Diagnostics src\Core\Ignixa.TestScript.Locust\Compilation\LocustSupportAnalyzer.cs test\Ignixa.TestScript.Locust.Tests\Compilation\LocustSupportAnalyzerTests.cs
git commit -m "Analyze TestScript Locust compatibility"
```

## Task 3: Lower operations, variables, and assertions into IR

**Files:**
- Create: `src/Core/Ignixa.TestScript.Locust/Compilation/LocustCompilerOptions.cs`
- Create: `src/Core/Ignixa.TestScript.Locust/Compilation/LocustCompilationResult.cs`
- Create: `src/Core/Ignixa.TestScript.Locust/Compilation/LocustIrCompiler.cs`
- Create: `test/Ignixa.TestScript.Locust.Tests/Compilation/LocustIrCompilerTests.cs`

- [ ] **Step 1: Write failing lowering tests**

The tests must assert:

- stable IDs: `setup.0`, `test.0.action.0`, `teardown.0`
- stable metric names: `read.json::setup.0`, `read.json::test.0.action.0`, `read.json::teardown.0`
- method derivation matches `TestScriptEvaluator`
- all nine assertion criteria map to the correct discriminator
- variable extraction maps header/path/FHIRPath exactly
- headers, `sourceId`, response/request IDs, and `waitFor` survive lowering
- `encodeRequestUrl=false` survives lowering for runtime warning parity

Use this representative test:

```csharp
[Fact]
public async Task GivenSupportedDefinition_WhenCompiled_ThenLowersSemanticActions()
{
    var definition = new TestScriptDefinition
    {
        Metadata = new TestScriptMetadata { Name = "Read patient" },
        Variables =
        [
            new VariableDefinition
            {
                Name = "patientId",
                SourceId = "created",
                Extraction = new ExpressionExtraction("Patient.id")
            }
        ],
        Tests =
        [
            new TestPhaseDefinition
            {
                Name = "read",
                Actions =
                [
                    new OperationExpression
                    {
                        Type = "read",
                        Resource = "Patient",
                        Params = "/${patientId}",
                        ResponseId = "read-response"
                    },
                    new AssertExpression
                    {
                        Criteria = new FhirPathCriteria("Patient.id.exists()"),
                        SourceId = "read-response"
                    }
                ]
            }
        ]
    };

    var result = await new LocustIrCompiler().CompileAsync(
        definition,
        new LocustCompilerOptions(
            "read.json",
            "4.0",
            FhirVersion.R4.GetSchemaProvider(),
            0),
        CancellationToken.None);

    result.HasErrors.ShouldBeFalse();
    result.Document.ShouldNotBeNull();
    result.Document.Variables[0].ExtractionKind.ShouldBe(LocustIrVariableExtractionKind.FhirPath);
    var operation = result.Document.Tests[0].Actions[0].ShouldBeOfType<LocustIrOperation>();
    operation.Method.ShouldBe("GET");
    operation.Params.ShouldBe("/${patientId}");
    result.Document.Tests[0].Actions[1]
        .ShouldBeOfType<LocustIrAssertion>().Criteria.Kind.ShouldBe(LocustIrAssertionKind.FhirPath);
}
```

- [ ] **Step 2: Run the test and verify it fails**

Run:

```powershell
dotnet test test\Ignixa.TestScript.Locust.Tests\Ignixa.TestScript.Locust.Tests.csproj --filter FullyQualifiedName~LocustIrCompilerTests
```

Expected: compilation fails because compiler types do not exist.

- [ ] **Step 3: Add options and result types**

```csharp
// Compilation/LocustCompilerOptions.cs
using Ignixa.Abstractions;
namespace Ignixa.TestScript.Locust.Compilation;
public sealed record LocustCompilerOptions(
    string Source,
    string? FhirVersion,
    IFhirSchemaProvider Schema,
    int FixtureVariants);

// Compilation/LocustCompilationResult.cs
using Ignixa.TestScript.Locust.Diagnostics;
using Ignixa.TestScript.Locust.Ir;
namespace Ignixa.TestScript.Locust.Compilation;
public sealed record LocustCompilationResult(
    LocustIrDocument? Document,
    IReadOnlyList<LocustDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == LocustDiagnosticSeverity.Error);
}
```

- [ ] **Step 4: Implement semantic lowering**

Implement `CompileAsync` with this public contract:

```csharp
public sealed class LocustIrCompiler
{
    public async Task<LocustCompilationResult> CompileAsync(
        TestScriptDefinition definition,
        LocustCompilerOptions options,
        CancellationToken cancellationToken);
}
```

Required private methods:

```csharp
private static IReadOnlyList<LocustIrVariable> CompileVariables(
    IReadOnlyList<VariableDefinition> variables);
private static IReadOnlyList<LocustIrAction> CompileActions(
    IReadOnlyList<ActionExpression> actions,
    string idPrefix);
private static LocustIrOperation CompileOperation(OperationExpression operation, string id);
private static LocustIrAssertion CompileAssertion(AssertExpression assertion, string id);
private static LocustIrAssertionCriteria CompileCriteria(AssertCriteria criteria);
private static string DeriveMethod(OperationExpression operation);
```

Copy the evaluator's method table exactly:

```csharp
private static string DeriveMethod(OperationExpression operation) =>
    (operation.Method ?? operation.Type switch
    {
        "create" => HttpMethod.Post,
        "read" or "vread" or "search" or "history" or "capabilities" or "conforms"
            => HttpMethod.Get,
        "update" or "updateCreate" => HttpMethod.Put,
        "patch" => HttpMethod.Patch,
        "delete" => HttpMethod.Delete,
        _ => HttpMethod.Post
    }).Method;
```

Map operators with `operator?.ToString()` and let the JSON enum/string adapter normalize them in
Python. Map `ResponseStatusCondition` into `WhenResponseSourceId` and `WhenResponseStatuses`.

Call `LocustSupportAnalyzer.Analyze` first. If it returns an error, return a result with a null
document and do not lower partially.

For every lowered operation, ungrouped assertion, and first member of each any-of group, append a
`LOCUST_METRIC` informational diagnostic whose message is
`Metric '<source>::<action-id>'` and whose `Source` is the canonical action source used by the
analyzer. Later members of a group do not get mappings because they emit no independent event. Add
the same mapping for fixture lifecycle metrics:

```text
<source>::fixture.<fixture-id>.autocreate
<source>::fixture.<fixture-id>.autodelete
```

The Python runtime must use the exact `<metadata.source>::<id>` function for HTTP request names,
`TESTSCRIPT_ASSERT`, and `TESTSCRIPT_OPERATION`; polling retries retain the original operation name.

- [ ] **Step 5: Run compiler tests**

Run:

```powershell
dotnet test test\Ignixa.TestScript.Locust.Tests\Ignixa.TestScript.Locust.Tests.csproj --filter FullyQualifiedName~LocustIrCompilerTests
```

Expected: PASS for all nine criteria, operations, variables, headers, and stable IDs.

- [ ] **Step 6: Commit**

```powershell
git add src\Core\Ignixa.TestScript.Locust\Compilation test\Ignixa.TestScript.Locust.Tests\Compilation\LocustIrCompilerTests.cs
git commit -m "Lower TestScript semantics to Locust IR"
```

## Task 4: Expand gates, parameters, and fixture variants

**Files:**
- Create: `src/Core/Ignixa.TestScript.Locust/Compilation/LocustFixtureCompiler.cs`
- Create: `test/Ignixa.TestScript.Locust.Tests/Compilation/LocustFixtureCompilerTests.cs`
- Create: `src/Core/Ignixa.TestScript/Evaluation/TestScriptVersionCompatibility.cs`
- Modify: `src/Core/Ignixa.TestScript/Evaluation/TestScriptEvaluator.cs`
- Modify: `src/Core/Ignixa.TestScript/Ignixa.TestScript.csproj`
- Modify: `src/Core/Ignixa.TestScript.Locust/Compilation/LocustIrCompiler.cs`
- Modify: `test/Ignixa.TestScript.Locust.Tests/Compilation/LocustIrCompilerTests.cs`

- [ ] **Step 1: Write failing tests**

Add tests for:

1. `fhirVersions` excludes a nonmatching test.
2. `parametrize` produces one `LocustIrTest` per value, binds `InitialVariables`, and assigns unique
   index-based IDs even when values repeat.
3. literal fixtures produce one variant.
4. `fhirfakes` with `FixtureVariants == 0` produces `LOCUST007`.
5. `fhirfakes` with `FixtureVariants == 3` invokes generation three times and produces three
   schema-valid resources.
6. suite/test `requiresCapability` expressions remain in IR.

The compiler integration test verifies count and schema validity:

```csharp
[Fact]
public async Task GivenFhirFakesFixture_WhenVariantCountProvided_ThenEmitsRequestedPool()
{
    var definition = TestDefinitions.WithFhirFakesPatientFixture();

    var result = await new LocustIrCompiler().CompileAsync(
        definition,
        new LocustCompilerOptions(
            "fakes.json",
            "4.0",
            FhirVersion.R4.GetSchemaProvider(),
            3),
        CancellationToken.None);

    result.HasErrors.ShouldBeFalse();
    result.Document!.Fixtures.Single().Variants.Count.ShouldBe(3);
    result.Document.Fixtures.Single().Variants
        .ShouldAllBe(resource => resource["resourceType"]!.GetValue<string>() == "Patient");
}
```

In `LocustFixtureCompilerTests`, inject a sequence fixture provider that returns Patient IDs `v1`,
`v2`, and `v3`; assert the provider was called three times and all three serialized variants are
present. This proves the pool does not clone one generated resource without relying on randomness.

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test test\Ignixa.TestScript.Locust.Tests\Ignixa.TestScript.Locust.Tests.csproj --filter "FullyQualifiedName~LocustFixtureCompilerTests|FullyQualifiedName~LocustIrCompilerTests"
```

Expected: fixture and expansion assertions fail.

- [ ] **Step 3: Implement fixture compilation**

`LocustFixtureCompiler` receives `options.Schema` and uses
`FhirFakesFixtureProvider` before `InlineFixtureProvider`:

```csharp
public sealed class LocustFixtureCompiler
{
    private readonly IFhirSchemaProvider _schema;
    private readonly IFixtureProvider _generated;
    private readonly IFixtureProvider _inline;

    public LocustFixtureCompiler(IFhirSchemaProvider schema)
        : this(schema, new FhirFakesFixtureProvider(), new InlineFixtureProvider())
    {
    }

    internal LocustFixtureCompiler(
        IFhirSchemaProvider schema,
        IFixtureProvider generated,
        IFixtureProvider inline)
    {
        _schema = schema;
        _generated = generated;
        _inline = inline;
    }

    public async Task<(LocustIrFixture? Fixture, LocustDiagnostic? Diagnostic)> CompileAsync(
        FixtureDefinition fixture,
        int variantCount,
        string source,
        CancellationToken cancellationToken)
    {
        var context = new FixtureResolutionContext
        {
            Schema = _schema,
            ResourceType = fixture.Resource?.ResourceType
        };
        var firstGenerated = await _generated.ResolveFixtureAsync(fixture, context, cancellationToken);
        var generated = firstGenerated is not null;
        if (generated && variantCount < 1)
            return (null, new LocustDiagnostic("LOCUST007", LocustDiagnosticSeverity.Error, source,
                "fhirfakes fixtures require --fixture-variants greater than zero."));

        var count = generated ? variantCount : 1;
        var variants = new List<JsonObject>(count);
        for (var index = 0; index < count; index++)
        {
            var resource = index switch
            {
                0 when firstGenerated is not null => firstGenerated,
                0 => await _inline.ResolveFixtureAsync(fixture, context, cancellationToken),
                _ => await _generated.ResolveFixtureAsync(fixture, context, cancellationToken)
            };
            if (resource is null)
                return (null, new LocustDiagnostic("LOCUST008", LocustDiagnosticSeverity.Error, source,
                    $"Fixture '{fixture.Id}' could not be materialized."));
            variants.Add(JsonNode.Parse(resource.SerializeToString())!.AsObject());
        }

        return (new LocustIrFixture(fixture.Id, fixture.Autocreate, fixture.Autodelete, variants), null);
    }
}
```

- [ ] **Step 4: Expand tests and preserve capability expressions**

In `LocustIrCompiler`:

- Skip a test when `FhirVersions` is nonempty and does not contain `options.FhirVersion`.
- Emit one test unchanged when `Parameters` is null.
- Emit one test per parameter value when non-null.
- Use `test.<test-index>` for a non-parameterized test and
  `test.<test-index>.param.<value-index>` for each expansion; derive action IDs from that full test
  ID. Never derive identifiers from parameter text.
- Set each expanded test's name to `"{Name} [{value}]"`.
- Add `{ Parameters.VariableName: value }` to `InitialVariables`.
- Set `DiscardContextAfterExecution = true` only for parameter expansions; ordinary tests keep the
  default `false`.
- Copy suite/test `RequiresCapability` into the IR without evaluating it.
- Compile every fixture through `LocustFixtureCompiler`.

Move `IsVersionCompatible`, `VersionMismatchReason`, `MatchesVersionSpec`, and
`TryParseVersionSpec` from `TestScriptEvaluator` into:

```csharp
namespace Ignixa.TestScript.Evaluation;

internal static class TestScriptVersionCompatibility
{
    public static bool IsCompatible(IReadOnlyList<string> fhirVersions, string? fhirVersion);
    public static string MismatchReason(IReadOnlyList<string> fhirVersions, string? fhirVersion);
}
```

Keep the existing private parsing methods inside that helper without changing their bodies. Update
`TestScriptEvaluator` to call `TestScriptVersionCompatibility.IsCompatible` and
`TestScriptVersionCompatibility.MismatchReason`. Add:

```xml
<InternalsVisibleTo Include="Ignixa.TestScript.Locust" />
```

to `Ignixa.TestScript.csproj`, then call the same `IsCompatible` method from
`LocustIrCompiler`. Do not duplicate the matching algorithm. Existing evaluator tests remain the
characterization suite; compiler tests must cover `4.0`, `4.0.1`, `4.3`, and `5.0`.

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test test\Ignixa.TestScript.Locust.Tests\Ignixa.TestScript.Locust.Tests.csproj --filter "FullyQualifiedName~LocustFixtureCompilerTests|FullyQualifiedName~LocustIrCompilerTests"
dotnet test test\Ignixa.TestScript.Tests\Ignixa.TestScript.Tests.csproj --filter FullyQualifiedName~TestScriptEvaluatorTests
```

Expected: both commands PASS.

- [ ] **Step 6: Commit**

```powershell
git add src\Core\Ignixa.TestScript src\Core\Ignixa.TestScript.Locust\Compilation test\Ignixa.TestScript.Locust.Tests\Compilation
git commit -m "Compile TestScript extensions and fixture variants"
```

## Task 5: Generate a flat Locust artifact

**Files:**
- Create: `src/Core/Ignixa.TestScript.Locust/Artifacts/LocustArtifactWriter.cs`
- Create: `src/Core/Ignixa.TestScript.Locust/Python/locustfile.py`
- Create: `src/Core/Ignixa.TestScript.Locust/Python/ignixa_testscript_runtime.py`
- Create: `src/Core/Ignixa.TestScript.Locust/Python/requirements.txt`
- Create: `test/Ignixa.TestScript.Locust.Tests/Artifacts/LocustArtifactWriterTests.cs`

- [ ] **Step 1: Write the failing artifact test**

```csharp
[Fact]
public async Task GivenDocument_WhenWritten_ThenArtifactIsFlatAndComplete()
{
    var output = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    var document = new LocustIrDocument
    {
        Metadata = new LocustIrMetadata("Basic", "basic.json", "4.0")
    };

    await new LocustArtifactWriter().WriteAsync(document, [], output, CancellationToken.None);

    Directory.GetDirectories(output).ShouldBeEmpty();
    Directory.GetFiles(output).Select(Path.GetFileName).Order()
        .ShouldBe([
            "diagnostics.json",
            "ignixa_testscript_runtime.py",
            "locustfile.py",
            "requirements.txt",
            "testscript.ir.json"
        ]);
}
```

Add a second test that creates an existing output directory containing `sentinel.txt`, injects an
asset-copy failure, and proves the original directory remains unchanged. Then run a successful write
and prove the sentinel is replaced by exactly the five artifact files.

Use a parameterless production constructor plus this internal test seam:

```csharp
internal LocustArtifactWriter(Func<string, Stream> openEmbeddedAsset);
```

The production constructor resolves resources from the compiler assembly; the failure test supplies
a delegate that throws `IOException`.

- [ ] **Step 2: Run the test and verify failure**

Run:

```powershell
dotnet test test\Ignixa.TestScript.Locust.Tests\Ignixa.TestScript.Locust.Tests.csproj --filter FullyQualifiedName~LocustArtifactWriterTests
```

Expected: compilation fails because the writer does not exist.

- [ ] **Step 3: Add pinned Python requirements**

Create `requirements.txt`:

```text
locust==2.33.2
fhirpathpy==2.1.0
requests==2.32.3
```

Do not use `fhirpathpy` 2.2.x: PyPI metadata requires Python 3.10+, while Azure Load Testing
currently pins Python 3.9.19. A future upgrade requires rerunning the compatibility contracts before
changing this pin.

- [ ] **Step 4: Add the fixed Locust loader**

Create `locustfile.py`:

```python
import json
import os
from pathlib import Path

from locust import HttpUser, between, task

import ignixa_testscript_runtime as runtime


_IR_PATH = Path(__file__).with_name("testscript.ir.json")
_DOCUMENT = json.loads(_IR_PATH.read_text(encoding="utf-8"))


class IgnixaTestScriptUser(HttpUser):
    wait_time = between(
        float(os.getenv("IGNIXA_WAIT_MIN_SECONDS", "0.5")),
        float(os.getenv("IGNIXA_WAIT_MAX_SECONDS", "1.5")),
    )
    host = os.getenv("IGNIXA_BASE_URL")

    def on_start(self):
        self.ignixa_state = runtime.initialize_user(_DOCUMENT, self)

    @task
    def execute_testscript(self):
        runtime.execute(_DOCUMENT, self, self.ignixa_state)
```

Create the initial runtime asset:

```python
import itertools


SUPPORTED_SCHEMA_MAJOR = 1
_USER_ORDINALS = itertools.count()


def initialize_user(document, user):
    major = int(document["schemaVersion"].split(".", 1)[0])
    if major != SUPPORTED_SCHEMA_MAJOR:
        raise RuntimeError(
            f"Unsupported TestScript IR schema {document['schemaVersion']}"
        )
    return {
        "iteration": 0,
        "ordinal": next(_USER_ORDINALS),
        "user": user,
    }


def execute(document, user, state):
    state["iteration"] += 1
    raise RuntimeError("Runtime execution is not implemented")
```

- [ ] **Step 5: Implement atomic asset writing**

`LocustArtifactWriter.WriteAsync` must:

1. Create a sibling temporary directory.
2. Write IR and diagnostics as UTF-8 without BOM.
3. Copy the three embedded Python assets by exact manifest suffix.
4. Reject any duplicate output filename.
5. After every write succeeds, move an existing output directory to a sibling backup.
6. Move the completed temporary directory into place.
7. Delete the backup only after the new output is in place.
8. If the swap fails, remove any partial new output, restore the backup, and rethrow.
9. Delete temporary/backup directories on failure without touching the original output before swap.

Serialize `diagnostics.json` as an indented camelCase array with severity values `info`, `warning`,
or `error`, using UTF-8 without BOM.

Do not catch and convert I/O failures into success-shaped diagnostics.

- [ ] **Step 6: Run artifact tests**

Run:

```powershell
dotnet test test\Ignixa.TestScript.Locust.Tests\Ignixa.TestScript.Locust.Tests.csproj --filter FullyQualifiedName~LocustArtifactWriterTests
```

Expected: PASS and no subdirectories under the generated artifact.

- [ ] **Step 7: Commit**

```powershell
git add src\Core\Ignixa.TestScript.Locust\Artifacts src\Core\Ignixa.TestScript.Locust\Python test\Ignixa.TestScript.Locust.Tests\Artifacts
git commit -m "Generate flat Locust test artifacts"
```

## Task 6: Add the `compile-locust` CLI command

**Files:**
- Create: `tools/Ignixa.ConformanceMatrix.Cli/Commands/CompileLocustCommand.cs`
- Create: `test/Ignixa.ConformanceMatrix.Cli.Tests/CompileLocustCommandTests.cs`
- Modify: `tools/Ignixa.ConformanceMatrix.Cli/Ignixa.ConformanceMatrix.Cli.csproj`
- Modify: `tools/Ignixa.ConformanceMatrix.Cli/Program.cs`

- [ ] **Step 1: Write failing CLI tests**

Test:

- missing `--test` returns usage exit code 2
- missing/invalid `--fhir-version` returns 2
- existing output directory is replaced only after successful compilation
- parser/analyzer errors return 1 and print every source-qualified diagnostic
- parser warnings are printed and persisted in `diagnostics.json`
- successful compilation writes the five flat files and returns 0
- `fhirfakes` without `--fixture-variants` returns 1

Use a temporary TestScript JSON file in each test; do not depend on repository-relative paths.

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test test\Ignixa.ConformanceMatrix.Cli.Tests\Ignixa.ConformanceMatrix.Cli.Tests.csproj --filter FullyQualifiedName~CompileLocustCommandTests
```

Expected: compilation fails because the command does not exist.

- [ ] **Step 3: Reference the compiler and register the command**

Add:

```xml
<ProjectReference Include="..\..\src\Core\Ignixa.TestScript.Locust\Ignixa.TestScript.Locust.csproj" />
```

Register in `Program.Main`:

```csharp
root.Subcommands.Add(CompileLocustCommand.Build());
```

- [ ] **Step 4: Implement the command**

Command contract:

```text
ignixa-matrix compile-locust
  --test <TestScript.json>
  --out <artifact-directory>
  --fhir-version <4.0|4.3|5.0>
  [--fixture-variants <positive integer>]
```

Resolve the schema exactly as follows, rejecting unsupported input before compilation:

```csharp
if (fhirVersion is not ("4.0" or "4.3" or "5.0"))
{
    Console.Error.WriteLine($"error: unsupported --fhir-version '{fhirVersion}'; expected 4.0, 4.3, or 5.0");
    return UsageErrorExitCode;
}

var version = FhirSpecificationExtensions.FromVersionString(fhirVersion);
var schema = version.GetSchemaProvider();
```

Then parse with `TestScriptParser.ParseFile`. Convert every `ParseError` into a
`LocustDiagnostic` using code `TESTSCRIPT_PARSE`, the corresponding warning/error severity, and
source `$"{testPath}:{error.Path ?? "$"}"`. Print all parser/compiler diagnostics. On successful
parsing and compilation, concatenate parser warnings with compiler diagnostics and pass the complete
list to `LocustArtifactWriter.WriteAsync`. Write only when neither list contains an error.

Pass `Path.GetFileName(testPath)` as `LocustCompilerOptions.Source` so metric names do not contain
machine-specific absolute paths. Print warning/error diagnostics to the console; persist
informational metric mappings only in `diagnostics.json`.

Construct options with the same resolved schema:

```csharp
var options = new LocustCompilerOptions(
    Path.GetFileName(testPath),
    fhirVersion,
    schema,
    fixtureVariants ?? 0);
```

Expose `internal static Task<int> RunAsync(...)` for tests. Use exit codes:

- 0 success
- 1 parse or compatibility failure
- 2 invalid invocation/path/version
- 3 unexpected internal failure

Preserve `OperationCanceledException`; do not convert cancellation into exit code 0.

- [ ] **Step 5: Run CLI tests**

Run:

```powershell
dotnet test test\Ignixa.ConformanceMatrix.Cli.Tests\Ignixa.ConformanceMatrix.Cli.Tests.csproj --filter FullyQualifiedName~CompileLocustCommandTests
```

Expected: PASS.

- [ ] **Step 6: Compile a shipped suite**

Run:

```powershell
dotnet run --project tools\Ignixa.ConformanceMatrix.Cli -- compile-locust --test src\Core\Ignixa.TestScript.Suites\testscripts\CRUD\basic.json --out artifacts\locust-basic --fhir-version 4.0 --fixture-variants 10
Get-ChildItem artifacts\locust-basic
```

Expected: exactly five files and no directories. Delete `artifacts\locust-basic` afterward.

- [ ] **Step 7: Commit**

```powershell
git add tools\Ignixa.ConformanceMatrix.Cli test\Ignixa.ConformanceMatrix.Cli.Tests
git commit -m "Add TestScript Locust compiler command"
```

## Task 7: Implement Python lifecycle and execution isolation

**Files:**
- Modify: `src/Core/Ignixa.TestScript.Locust/Python/ignixa_testscript_runtime.py`
- Create: `test/Ignixa.TestScript.Locust.Tests/Python/fakes.py`
- Create: `test/Ignixa.TestScript.Locust.Tests/Python/test_runtime_lifecycle.py`

- [ ] **Step 1: Create Python fakes and failing lifecycle tests**

`fakes.py` must expose:

```python
import importlib.util
from pathlib import Path


def load_runtime():
    runtime_path = (
        Path(__file__).resolve().parents[3]
        / "src"
        / "Core"
        / "Ignixa.TestScript.Locust"
        / "Python"
        / "ignixa_testscript_runtime.py"
    )
    spec = importlib.util.spec_from_file_location(
        "ignixa_testscript_runtime_under_test",
        runtime_path,
    )
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load runtime from {runtime_path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class FakeRequestEvents:
    def __init__(self):
        self.items = []

    def fire(self, **kwargs):
        self.items.append(kwargs)


class FakeEnvironment:
    def __init__(self):
        self.events = type(
            "Events",
            (),
            {"request": FakeRequestEvents()},
        )()


class FakeUser:
    def __init__(self, client):
        self.client = client
        self.environment = FakeEnvironment()
        self.host = "http://example.test"
```

Each test calls `load_runtime()` in `setUp` so user ordinals, capability decisions, and other
engine-local module state cannot leak between tests. Do not modify `PYTHONPATH` or depend on the
process working directory.

Write tests proving:

- schema major mismatch raises before execution
- setup, tests, teardown run in order
- setup failure skips tests but still follows evaluator teardown policy
- each `execute` call creates new variables/fixtures/history
- two users share no execution context
- parameter expansions inherit setup state but discard their variable/history mutations before the
  next expansion; ordinary tests retain mutations
- fixture selection is deterministic for the same hash inputs and matches fixed expected indices

- [ ] **Step 2: Run tests and verify failure**

Run with Python 3.9:

```powershell
py -3.9 -m unittest discover -s test\Ignixa.TestScript.Locust.Tests\Python -p "test_runtime_lifecycle.py" -v
```

Expected: FAIL because runtime still throws "not implemented".

- [ ] **Step 3: Implement lifecycle functions**

Keep state per user limited to user ordinal and iteration. Create a fresh execution dictionary:

```python
def _new_context(document, user_state):
    return {
        "variables": {
            item["name"]: item["defaultValue"]
            for item in document.get("variables", [])
            if item.get("defaultValue") is not None
        },
        "fixtures": {},
        "requests": {},
        "responses": {},
        "last_request": None,
        "last_response": None,
        "user_state": user_state,
    }
```

Implement `execute` with `try/finally` so teardown runs after setup/test execution according to the
evaluator's behavior. Return an execution outcome dictionary for tests; Locust ignores the return
value.

Run every action in a phase even after a recorded failure. Aggregate phase failure separately:

- a failed operation marks its current phase failed
- a failed non-warning assertion marks its current phase failed
- an inapplicable or failed warning-only assertion does not mark the phase failed
- any setup failure skips every test but not teardown
- test failures do not skip later actions, later tests, or teardown
- suite capability rejection returns before fixtures, setup, tests, or teardown

For a test with `discardContextAfterExecution=true`, clone the four context dictionaries
(`variables`, `fixtures`, `requests`, `responses`) plus last request/response references, apply
`initialVariables`, execute against the clone, and discard it. For an ordinary test, execute against
the shared invocation context so its state remains visible to later tests.

Do not use exceptions for normal failed TestScript outcomes; reserve them for invalid runtime
configuration and unrecoverable interpreter defects.

Fixture variant selection must hash:

```text
IGNIXA_FIXTURE_SEED | hostname | user ordinal | iteration | fixture id
```

Join the five UTF-8 values with literal `|` and no surrounding spaces, compute SHA-256, convert the
entire digest with `int.from_bytes(digest, "big")`, and take modulo pool length. Do not use Python's
randomized `hash()`. Pin this fixture-selection test:

```python
self.assertEqual(
    [1, 1, 2, 0, 0, 2],
    [
        runtime._fixture_variant_index("", "engine-a", 0, iteration, "patient", 3)
        for iteration in range(1, 7)
    ],
)
```

- [ ] **Step 4: Run lifecycle tests**

Run:

```powershell
py -3.9 -m unittest discover -s test\Ignixa.TestScript.Locust.Tests\Python -p "test_runtime_lifecycle.py" -v
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.TestScript.Locust\Python\ignixa_testscript_runtime.py test\Ignixa.TestScript.Locust.Tests\Python
git commit -m "Execute isolated TestScript lifecycle in Locust"
```

## Task 8: Execute operations, fixtures, variables, and polling

**Files:**
- Modify: `src/Core/Ignixa.TestScript.Locust/Python/ignixa_testscript_runtime.py`
- Modify: `test/Ignixa.TestScript.Locust.Tests/Python/fakes.py`
- Create: `test/Ignixa.TestScript.Locust.Tests/Python/test_runtime_operations.py`

- [ ] **Step 1: Extend the fake HTTP client**

Implement `FakeClient.request` with queued responses and request capture. `FakeResponse` must expose:

- `status_code`
- `headers`
- `content`
- `json()`
- `text`

Capture method, URL, headers, JSON body, form body, and Locust request name.

- [ ] **Step 2: Write failing operation tests**

Cover:

- CRUD method/URL derivation and explicit URLs
- POST search strips one or more leading `?` characters and sends the remaining text as a form body
- request headers with variable substitution
- `IGNIXA_AUTH_HEADER` is applied to FHIR requests and an explicit TestScript header overrides it
- `sourceId` from fixture and prior response
- request/response history IDs
- source-qualified HTTP, fixture, and `TESTSCRIPT_OPERATION` metric names
- received 4xx/5xx responses remain successful HTTP events until TestScript assertions evaluate them
- transport exceptions remain failed native HTTP events without a duplicate semantic event
- `encodeRequestUrl=false` logs a warning and does not emit a failed event
- header, dotted-path, and FHIRPath variable extraction
- missing header/path extraction remains a no-op
- malformed FHIRPath extraction emits `TESTSCRIPT_OPERATION`
- fixture autocreate replaces the fixture with the server response
- fixture autocreate updates response history under the fixture ID and runs variable extraction
- fixture autodelete uses server-assigned resource ID
- `waitFor` sends N attempts, uses the same metric name, and emits semantic failure on exhaustion

- [ ] **Step 3: Run tests and verify failure**

Run:

```powershell
py -3.9 -m unittest discover -s test\Ignixa.TestScript.Locust.Tests\Python -p "test_runtime_operations.py" -v
```

Expected: FAIL because operation functions are absent.

- [ ] **Step 4: Implement request execution**

Add focused functions:

```python
def _resolve(template, context):
    if template is None:
        return None
    pattern = re.compile(r"\$\{([^}]+)\}")

    def replace(match):
        name = match.group(1)
        if name not in context["variables"]:
            raise RuntimeError(f"Variable '{name}' is not defined")
        return str(context["variables"][name])

    return pattern.sub(replace, template)


def _derive_url(operation, context):
    if operation.get("url") is not None:
        return _resolve(operation["url"], context)
    resource = operation.get("resource") or ""
    if operation["type"] == "search" and operation["method"] == "POST":
        return f"{resource}/_search"
    params = _resolve(operation.get("params"), context) or ""
    if operation["type"].startswith("$"):
        path = operation["type"] if not resource else f"{resource}/{operation['type']}"
        return f"{path}{params}"
    return f"{resource}{params}"
```

Use `user.client.request(..., catch_response=True)` for every send. When the server returns any HTTP
response, including 4xx/5xx, call `response.success()` and store the response for TestScript
assertions. Do not call `raise_for_status`; this matches `TestScriptEvaluator`, where a received
response is a successful operation and response assertions determine conformance. Leave transport
exceptions as native failed Locust HTTP events and mark the operation failed without emitting a
duplicate semantic failure.

Store actual request/response wrappers in context. Use `gevent.sleep(interval_seconds)` for polling.
Emit `TESTSCRIPT_OPERATION` with zero response time when semantic operation failure is not already
represented by a failed HTTP event. Name every HTTP and semantic event with:

```python
def _metric_name(document, action_id):
    return f"{document['metadata']['source']}::{action_id}"
```

Use `fixture.<fixture-id>.autocreate` and `fixture.<fixture-id>.autodelete` as the action IDs for
implicit lifecycle requests.

When `encodeRequestUrl` is false, log a warning through `ignixa.testscript` with the metric name and
the same “URL was encoded” meaning as the evaluator. Do not emit a failed event or disable normal
requests/Locust URL encoding.

Add `_parse_auth_header()` in this task. Parse `IGNIXA_AUTH_HEADER` as exactly one `Name: value` pair
using `partition(":")`; reject a missing colon, empty name, or empty value with `RuntimeError`.
Build headers with `requests.structures.CaseInsensitiveDict`: add the configured pair first, then
apply the TestScript operation's resolved headers so an explicit script header with the same
case-insensitive name wins.

Match `HttpTestRequestProvider` content handling:

- POST search form body always uses `application/x-www-form-urlencoded; charset=utf-8`, overriding
  a TestScript `Content-Type`, and sends the resolved text as UTF-8 bytes
- a JSON resource body uses the resolved TestScript `Content-Type` or
  `application/fhir+json; charset=utf-8`, and sends
  `json.dumps(resource, separators=(",", ":")).encode("utf-8")`
- a request without a body removes `Content-Type`
- `Accept` and non-content headers remain unchanged

- [ ] **Step 5: Implement fixture lifecycle and extraction**

Port the evaluator's exact rules:

- autocreate is POST to `resourceType`
- 2xx succeeds
- response body replaces fixture when present
- autodelete requires resource type and server ID
- header/path misses do not fail
- FHIRPath extraction exceptions fail the operation
- numeric/boolean JSON leaves serialize as JSON text (`3`, `true`)
- terminal object/array path values use `json.dumps(value, separators=(",", ":"))`; paths do not
  traverse array indices

- [ ] **Step 6: Run operation tests**

Run:

```powershell
py -3.9 -m unittest discover -s test\Ignixa.TestScript.Locust.Tests\Python -p "test_runtime_operations.py" -v
```

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src\Core\Ignixa.TestScript.Locust\Python\ignixa_testscript_runtime.py test\Ignixa.TestScript.Locust.Tests\Python
git commit -m "Execute TestScript operations in Locust"
```

## Task 9: Implement assertions, alternatives, gates, and FHIRPath

**Files:**
- Modify: `src/Core/Ignixa.TestScript.Locust/Python/ignixa_testscript_runtime.py`
- Modify: `src/Core/Ignixa.TestScript.Locust/Python/locustfile.py`
- Modify: `src/Core/Ignixa.TestScript.Locust/Ignixa.TestScript.Locust.csproj`
- Modify: `src/Core/Ignixa.TestScript.Locust/Compilation/LocustIrCompiler.cs`
- Modify: `test/Ignixa.TestScript.Locust.Tests/Compilation/LocustIrCompilerTests.cs`
- Create: `src/Core/Ignixa.TestScript.Locust/Compatibility/FhirPathCompatibilityManifest.cs`
- Create: `src/Core/Ignixa.TestScript.Locust/Compatibility/FhirPathIncompatibility.cs`
- Create: `src/Core/Ignixa.TestScript.Locust/Compatibility/FhirPathUsage.cs`
- Create: `src/Core/Ignixa.TestScript.Locust/Compatibility/fhirpath-incompatibilities.json`
- Create: `test/Ignixa.TestScript.Locust.Tests/Python/test_runtime_assertions.py`
- Create: `test/Ignixa.TestScript.Locust.Tests/Contracts/fhirpath-cases.json`
- Create: `test/Ignixa.TestScript.Locust.Tests/Contracts/FhirPathContractTests.cs`
- Create: `test/Ignixa.TestScript.Locust.Tests/Python/test_fhirpath_contract.py`

- [ ] **Step 1: Add shared FHIRPath cases**

Start with expressions used by shipped TestScripts and coercions known to be sensitive:

```json
[
  {
    "name": "boolean-exists",
    "resource": {"resourceType": "Patient", "id": "1"},
    "expression": "Patient.id.exists()",
    "shape": "boolean",
    "expected": true
  },
  {
    "name": "boolean-lowercase",
    "resource": {"resourceType": "Patient", "active": true},
    "expression": "Patient.active",
    "shape": "scalar",
    "expected": "true"
  },
  {
    "name": "empty-scalar",
    "resource": {"resourceType": "Patient"},
    "expression": "Patient.id",
    "shape": "scalar",
    "expected": null
  }
]
```

Add one case for every distinct FHIRPath expression and evaluation shape found by scanning
`src/Core/Ignixa.TestScript.Suites/testscripts/**/*.json`, including `requiresCapability`,
assertions, and variable extraction. Each case must include a representative resource that exercises
the expression rather than merely proving it parses.

- [ ] **Step 2: Write failing C# and Python contract tests**

The C# test parses each resource with `JsonSourceNodeFactory`, evaluates using the requested
boolean/scalar adapter, and compares to `expected`.

The Python test loads the same JSON, calls `_evaluate_fhirpath`, and compares to `expected`.

Add a failing compiler test that constructs a manifest containing
`("Patient.name", FhirPathUsage.Scalar, "multi-value coercion differs")`, compiles a definition with
`new ExpressionExtraction("Patient.name")`, and asserts a source-qualified `LOCUST009` error and a
null document. Add a second failing compiler test using malformed suite/test capability, assertion,
and variable-extraction expressions; assert one source-qualified `LOCUST010` error per expression
and a null document.

Do not change an expected value to accommodate `fhirpathpy`. Record every
`fhirpathpy==2.1.0` mismatch as an exact expression/usage entry in
`fhirpath-incompatibilities.json`.

- [ ] **Step 3: Write failing assertion tests**

Cover all `LocustIrAssertionKind` values and all ten operators. Also cover:

- `warningOnly` failed assertion logs but emits no failed event
- inapplicable status-conditional assertion emits no request event
- any-of group passes when one applicable member passes
- any-of group errors when no member is applicable
- any-of group emits one event under its first member's metric name and no member events
- source IDs select prior requests/responses
- malformed response JSON becomes an assertion error with the parse reason
- suite gate disables all execution
- test gate skips only the test
- missing CapabilityStatement fails open
- malformed capability expression fails closed
- `IGNIXA_AUTH_HEADER` is applied to the uninstrumented metadata request
- malformed `IGNIXA_AUTH_HEADER` fails startup explicitly
- engine decisions and the user-ordinal counter are reset for each test run
- unsupported IR schema major fails during engine initialization

- [ ] **Step 4: Implement FHIRPath adapter**

Pin the call shape:

```python
from fhirpathpy import evaluate as evaluate_fhirpathpy


def _evaluate_fhirpath(expression, resource, shape):
    values = evaluate_fhirpathpy(resource, expression)
    if shape == "boolean":
        return len(values) == 1 and values[0] is True
    if not values:
        return None
    if len(values) != 1:
        return None
    value = values[0]
    if isinstance(value, bool):
        return "true" if value else "false"
    if value is None:
        return None
    return str(value)
```

If contract cases prove Ignixa uses a different multi-value coercion for a specific shape, encode that
behavior in this adapter and add the case before changing implementation.

- [ ] **Step 5: Implement the compile-time FHIRPath compatibility gate**

Create:

```csharp
// Compatibility/FhirPathUsage.cs
namespace Ignixa.TestScript.Locust.Compatibility;
internal enum FhirPathUsage { Boolean, Scalar }

// Compatibility/FhirPathIncompatibility.cs
namespace Ignixa.TestScript.Locust.Compatibility;
internal sealed record FhirPathIncompatibility(
    string Expression,
    FhirPathUsage Usage,
    string Reason);
```

Create `fhirpath-incompatibilities.json` as an empty reviewed denylist:

```json
[]
```

Add this embedded asset:

```xml
<EmbeddedResource Include="Compatibility\fhirpath-incompatibilities.json" />
```

`FhirPathCompatibilityManifest` must load that exact embedded-resource suffix with
`JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`, reject duplicate expression/usage pairs, and
expose:

```csharp
internal sealed class FhirPathCompatibilityManifest
{
    internal FhirPathCompatibilityManifest(
        IReadOnlyList<FhirPathIncompatibility> entries);
    public static FhirPathCompatibilityManifest LoadEmbedded();
    public string? FindReason(string expression, FhirPathUsage usage);
}
```

Give `LocustIrCompiler` a parameterless constructor that loads the embedded manifest and an internal
constructor accepting a manifest for tests. Before filtering or lowering, scan:

- suite/test `RequiresCapability` as `Boolean`
- `FhirPathCriteria` as `Boolean`
- `FhirPathValueCriteria` as `Scalar`
- `ExpressionExtraction` as `Scalar`

Parse every scanned expression with `new FhirPathParser().Parse(expression)`. Convert
`ArgumentException` and `FormatException` into source-qualified `LOCUST010` errors. Do not catch
other exceptions. This makes malformed source fail compilation; the Python runtime still handles
invalid expressions explicitly because emitted IR can be corrupted or edited after compilation.

For every manifest match, emit an error with code `LOCUST009`, the canonical action/variable source,
and the manifest reason. Return a null document when any match exists. Make the injected-manifest
and malformed-expression compiler tests from Step 2 pass.

- [ ] **Step 6: Implement assertions and events**

Port the evaluator's:

- response category table
- media-type comparison
- operator table, including invariant decimal comparison before ordinal string comparison
- request/response/body resolution
- warning-only handling
- status applicability
- any-of group buffering and aggregate outcome

Emit:

```python
environment.events.request.fire(
    request_type="TESTSCRIPT_ASSERT",
    name=_metric_name(document, assertion["id"]),
    response_time=0,
    response_length=0,
    exception=exception_or_none,
    context={"source": _metric_name(document, assertion["id"])},
)
```

Do not emit an event for skipped or failed warning-only assertions. Log failed warning-only
assertions through `logging.getLogger("ignixa.testscript").warning(...)` with the source-qualified
metric name and failure message; assert the log with `unittest.TestCase.assertLogs`.
Buffer each any-of group through its last member, then emit exactly one event using the first
member's source-qualified metric name. Do not emit individual member events.

- [ ] **Step 7: Implement capability initialization**

At Locust `events.test_start`, fetch `{host}/metadata` once per engine with an uninstrumented
`requests.Session`, apply `IGNIXA_AUTH_HEADER` when set, evaluate suite/test expressions, and cache
immutable decisions in module state. Clear decisions on `events.test_stop`.

Network/parse failure means all gates pass. Expression failure means the affected suite/test is
disabled with a structured error logged through `ignixa.testscript`, including the expression,
suite/test ID, and evaluator exception.

Fetch `f"{host.rstrip('/')}/metadata"` with a 30-second timeout inside a `with requests.Session()`
block, call `raise_for_status()`, and raise `ValueError` when `response.json()` is not a dictionary.
Catch only `requests.RequestException` and `ValueError` for the documented fail-open path; log the
reason at warning level. Do not retain the session or response.

Reuse `_parse_auth_header()` from operation execution for the metadata session. Do not maintain a
second parser or silently omit malformed authentication configuration.

Add the fixed loader hooks:

```python
from locust import HttpUser, between, events, task


@events.test_start.add_listener
def _initialize_engine(environment, **_kwargs):
    runtime.initialize_engine(_DOCUMENT, environment)


@events.test_stop.add_listener
def _clear_engine(**_kwargs):
    runtime.clear_engine()
```

`initialize_engine` resolves the target in this order:

1. `environment.host`, supplied by Locust `--host`.
2. `IGNIXA_BASE_URL`, also assigned to `IgnixaTestScriptUser.host`.
3. Raise `RuntimeError` stating that a target host is required.

Call the same schema-major validator used by `initialize_user` before fetching metadata so an
unsupported IR fails during `test_start`, before users spawn. The cached state contains only the
immutable suite decision and a test-ID-to-decision mapping. The
metadata body itself and `requests.Session` are not retained after initialization.
Clear stale decisions and reset `_USER_ORDINALS = itertools.count()` at the start of
`initialize_engine`, before validation or I/O. Clear both again at `test_stop`, so failed startup
cannot reuse prior decisions and identical seeds are reproducible across runs in a reused process.

- [ ] **Step 8: Run C# and Python tests**

Run:

```powershell
dotnet test test\Ignixa.TestScript.Locust.Tests\Ignixa.TestScript.Locust.Tests.csproj --filter FullyQualifiedName~FhirPathContractTests
dotnet test test\Ignixa.TestScript.Locust.Tests\Ignixa.TestScript.Locust.Tests.csproj --filter FullyQualifiedName~LocustIrCompilerTests
py -3.9 -m pip install -r src\Core\Ignixa.TestScript.Locust\Python\requirements.txt
py -3.9 -m unittest discover -s test\Ignixa.TestScript.Locust.Tests\Python -p "test_*assertions.py" -v
py -3.9 -m unittest discover -s test\Ignixa.TestScript.Locust.Tests\Python -p "test_fhirpath_contract.py" -v
```

Expected: PASS.

- [ ] **Step 9: Commit**

```powershell
git add src\Core\Ignixa.TestScript.Locust test\Ignixa.TestScript.Locust.Tests
git commit -m "Evaluate TestScript assertions in Locust"
```

## Task 10: Add cross-language runtime contracts

**Files:**
- Create: `test/Ignixa.TestScript.Locust.Tests/Contracts/runtime-cases.json`
- Create: `test/Ignixa.TestScript.Locust.Tests/Contracts/RuntimeContractTests.cs`
- Create: `test/Ignixa.TestScript.Locust.Tests/Python/test_runtime_contract.py`
- Modify: `test/Ignixa.TestScript.Locust.Tests/Ignixa.TestScript.Locust.Tests.csproj`

- [ ] **Step 1: Define deterministic contract cases**

Each case contains:

- input TestScript JSON
- canonical compiled IR
- queued HTTP responses
- expected outbound requests
- expected variable values
- expected phase outcomes
- expected assertion/operation events

Include CRUD, POST search, history lookup, polling success/timeout, warning-only, skipped assertion,
any-of groups, setup failure, and fixture autocreate/autodelete.

- [ ] **Step 2: Write the .NET contract test**

For each case:

1. Parse through `TestScriptParser`.
2. Execute through `TestScriptEvaluator` with a deterministic `ITestRequestProvider`.
3. Compile through `LocustIrCompiler`.
4. Compare evaluator requests/outcomes and compiler IR against the contract.

Do not write baselines during the test; committed JSON is the reviewed contract.

- [ ] **Step 3: Write the Python contract test**

For each case:

1. Load the compiled IR fixture from the contract.
2. Queue the same responses in `FakeClient`.
3. Execute one runtime iteration.
4. Compare requests, variables, and emitted events with the contract.

- [ ] **Step 4: Run both contract suites**

Run:

```powershell
dotnet test test\Ignixa.TestScript.Locust.Tests\Ignixa.TestScript.Locust.Tests.csproj --filter FullyQualifiedName~RuntimeContractTests
py -3.9 -m unittest discover -s test\Ignixa.TestScript.Locust.Tests\Python -p "test_runtime_contract.py" -v
```

Expected: all cases PASS with no target-specific exclusions.

- [ ] **Step 5: Commit**

```powershell
git add test\Ignixa.TestScript.Locust.Tests
git commit -m "Add TestScript Locust parity contracts"
```

## Task 11: Run Python 3.9 tests in CI

**Files:**
- Modify: `.github/workflows/pr-build.yml`
- Modify: `.github/workflows/ci.yml`

- [ ] **Step 1: Add Python setup and runtime test steps**

In each `build-and-unit-tests` job, add Python setup after checkout and before the .NET build action:

```yaml
      - name: Setup Python for TestScript Locust runtime
        uses: actions/setup-python@v6
        with:
          python-version: '3.9.19'
          cache: 'pip'
          cache-dependency-path: 'src/Core/Ignixa.TestScript.Locust/Python/requirements.txt'

      - name: Install TestScript Locust runtime dependencies
        run: |
          python -m pip install --disable-pip-version-check \
            -r src/Core/Ignixa.TestScript.Locust/Python/requirements.txt
```

Add runtime execution after the existing .NET build-and-test action. The composite action installs
the requested .NET SDK and builds the CLI before the generated-artifact smoke test invokes it:

```yaml
      - name: Test TestScript Locust runtime
        run: |
          python -m unittest discover \
            -s test/Ignixa.TestScript.Locust.Tests/Python \
            -p 'test_*.py' -v
```

- [ ] **Step 2: Validate workflow syntax locally**

Inspect the changed job steps in both workflow files and run:

```powershell
git diff --check
```

Expected: no whitespace or YAML indentation errors.

- [ ] **Step 3: Run all new tests locally**

Run:

```powershell
dotnet test test\Ignixa.TestScript.Locust.Tests\Ignixa.TestScript.Locust.Tests.csproj
dotnet test test\Ignixa.ConformanceMatrix.Cli.Tests\Ignixa.ConformanceMatrix.Cli.Tests.csproj
py -3.9 -m unittest discover -s test\Ignixa.TestScript.Locust.Tests\Python -p "test_*.py" -v
```

Expected: all pass.

- [ ] **Step 4: Commit**

```powershell
git add .github\workflows\pr-build.yml .github\workflows\ci.yml
git commit -m "Run Locust runtime contracts in CI"
```

## Task 12: Validate the generated artifact and document usage

**Files:**
- Modify: `docs/site/docs/core-sdk/testscript.md`
- Modify: `docs/features/testscript/investigations/azure-load-testing.md`
- Create: `test/Ignixa.TestScript.Locust.Tests/Python/test_generated_artifact.py`

- [ ] **Step 1: Add a generated-artifact smoke test**

The test must:

1. Compile `src/Core/Ignixa.TestScript.Suites/testscripts/CRUD/basic.json` to a temporary directory by
   running `dotnet run --project tools/Ignixa.ConformanceMatrix.Cli -- compile-locust ...`.
2. Assert the directory is flat.
3. Import the generated `locustfile.py`.
4. Run one user iteration against a deterministic local HTTP server.
5. Assert HTTP request names and assertion events are present.

Keep this test local and deterministic; do not require Azure credentials.

- [ ] **Step 2: Run the local smoke test**

Run:

```powershell
py -3.9 -m unittest discover -s test\Ignixa.TestScript.Locust.Tests\Python -p "test_generated_artifact.py" -v
```

Expected: PASS.

- [ ] **Step 3: Run an Azure Load Testing smoke**

Using an existing non-production Azure Load Testing resource:

1. Compile `CRUD/basic.json` with 10 fixture variants.
2. Upload all five files as one Locust test.
3. Configure two engine instances, 10 users, one-minute duration, and a non-production FHIR target.
4. Confirm both engines load `fhirpathpy==2.1.0`.
5. Confirm HTTP metrics use stable source-qualified names.
6. Confirm a deliberately failing assertion appears as an error.
7. Confirm fixture variants are distributed and teardown removes created resources.
8. Attempt an Azure failure criterion scoped to Locust request type and record whether
   `TESTSCRIPT_ASSERT` can be filtered independently from HTTP requests.
9. Record the Azure test run ID and observed limitations in the investigation evidence.

Do not run this against production or place credentials in the artifact.

- [ ] **Step 4: Document the command and boundaries**

Add to the TestScript SDK documentation:

```text
ignixa-matrix compile-locust \
  --test path/to/TestScript.json \
  --out artifacts/testscript-load \
  --fhir-version 4.0 \
  --fixture-variants 100
```

Document:

- one complete TestScript execution per user iteration
- current-evaluator parity, not full HL7 parity
- supported assertions and extensions
- `fhirpathpy==2.1.0` / Python 3.9.19 compatibility constraint
- why `fhir.resources` is not a runtime dependency or profile validator
- bounded `fhirfakes` pools
- runtime capability gating
- synthetic assertion/operation metrics
- source-qualified metric names and their `diagnostics.json` mappings
- original .NET run as the authoritative TestReport
- `IGNIXA_BASE_URL` or Locust `--host` for the target
- `IGNIXA_AUTH_HEADER` as a single `Name: value` metadata-request header
- `IGNIXA_FIXTURE_SEED` for reproducible fixture selection
- `IGNIXA_WAIT_MIN_SECONDS` and `IGNIXA_WAIT_MAX_SECONDS` for per-iteration wait time

- [ ] **Step 5: Update investigation status**

Keep the investigation verdict `Viable`; add an implementation evidence section linking the compiler
project, command, parity contracts, and Azure smoke run.

- [ ] **Step 6: Run final verification**

Run:

```powershell
dotnet build All.sln
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"
py -3.9 -m unittest discover -s test\Ignixa.TestScript.Locust.Tests\Python -p "test_*.py" -v
git diff --check
```

Expected: build succeeds with zero warnings/errors, all non-E2E .NET tests pass, all Python tests pass,
and the diff check reports no errors.

- [ ] **Step 7: Commit**

```powershell
git add docs test\Ignixa.TestScript.Locust.Tests\Python\test_generated_artifact.py
git commit -m "Document TestScript Locust compilation"
```

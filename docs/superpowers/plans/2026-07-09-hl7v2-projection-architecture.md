# HL7v2 Projection Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the current NHapi ADT^A01 prototype into an extensible HL7v2 projection architecture that supports stable FML-visible logical paths, version-aware projections, custom segment extension points, diagnostics, and an outbound boundary.

**Architecture:** Keep NHapi isolated inside `Ignixa.Hl7v2`. FML and StructureMaps depend only on Ignixa's `IElement` logical tree contract, not NHapi classes. Segment/datatype/message projections expose structure; FML and ConceptMaps own semantic mapping decisions.

**Tech Stack:** .NET 9/10, C#, NHapi, xUnit, Shouldly, `Ignixa.Abstractions.IElement`.

---

## File Structure

The plan evolves the prototype into these focused units:

```text
src\Core\Ignixa.Hl7v2\
  Ignixa.Hl7v2.csproj
  Parsing\
    Hl7v2ParseResult.cs
    IHl7v2Parser.cs
    NhapiHl7v2Parser.cs
  Projection\
    Hl7v2ProjectionContext.cs
    Hl7v2ProjectionRegistry.cs
    Hl7v2ProjectionResult.cs
    IHl7v2Projection.cs
  Projection\Messages\
    AdtA01Projection.cs
    AdtA08Projection.cs
  Projection\Segments\
    MshProjection.cs
    PidProjection.cs
    Pv1Projection.cs
    ZSegmentProjection.cs
  Projection\Datatypes\
    CxProjection.cs
    HdProjection.cs
    XpnProjection.cs
    FnProjection.cs
    TsProjection.cs
  LogicalModel\
    Hl7v2Element.cs
    Hl7v2ElementBuilder.cs
    Hl7v2LogicalPath.cs
  Encoding\
    Hl7v2EncodeResult.cs
    IHl7v2Encoder.cs
    NhapiHl7v2Encoder.cs
  Validation\
    Hl7v2Diagnostic.cs
    Hl7v2DiagnosticSeverity.cs
    Hl7v2ValidationResult.cs
  Acknowledgments\
    Hl7v2AckBuilder.cs

test\Ignixa.Hl7v2.Tests\
  Parsing\
    NhapiHl7v2ParserTests.cs
  Projection\
    Hl7v2ProjectionRegistryTests.cs
    AdtA01ProjectionTests.cs
    CustomSegmentProjectionTests.cs
  Encoding\
    NhapiHl7v2EncoderTests.cs
  Acknowledgments\
    Hl7v2AckBuilderTests.cs
```

---

### Task 1: Rename the current prototype language from Adapter to Projection

**Files:**
- Rename: `src\Core\Ignixa.Hl7v2\Adapters\NhapiAdtA01LogicalModelAdapter.cs` -> `src\Core\Ignixa.Hl7v2\Projection\Messages\AdtA01Projection.cs`
- Modify: `test\Ignixa.Hl7v2.Tests\NhapiAdtA01LogicalModelAdapterTests.cs`

- [ ] **Step 1: Rename the test class and API references**

Replace `test\Ignixa.Hl7v2.Tests\NhapiAdtA01LogicalModelAdapterTests.cs` with:

```csharp
/*
 * Copyright (c) 2026, Ignixa Contributors
 *
 * Prototype tests for projecting NHapi-parsed HL7v2 messages into an Ignixa logical tree.
 */

using Shouldly;
using Ignixa.Hl7v2.LogicalModel;
using Ignixa.Hl7v2.Projection.Messages;
using Xunit;

namespace Ignixa.Hl7v2.Tests;

public class AdtA01ProjectionTests
{
    private const string AdtA01 = """
        MSH|^~\&|SendingApp|SendingFacility|ReceivingApp|ReceivingFacility|202607091200||ADT^A01|MSG00001|P|2.5
        EVN|A01|202607091200
        PID|1||12345^^^MRN^MR||Doe^Jane^A||19800101|F
        PV1|1|I
        """;

    [Fact]
    public void GivenAdtA01Er7_WhenProjectingToLogicalTree_ThenExposesPatientPathsUsedByFml()
    {
        var projection = new AdtA01Projection();

        var message = projection.Project(AdtA01);

        message.Name.ShouldBe("msg");
        message.InstanceType.ShouldBe("Hl7v2AdtA01");
        Hl7v2LogicalPath.SelectSingle(message, "msg.PID.patientIdentifierList[0].idNumber")?.Value.ShouldBe("12345");
        Hl7v2LogicalPath.SelectSingle(message, "msg.PID.patientIdentifierList[0].assigningAuthority.namespaceId")?.Value.ShouldBe("MRN");
        Hl7v2LogicalPath.SelectSingle(message, "msg.PID.patientName[0].familyName.surname")?.Value.ShouldBe("Doe");
        Hl7v2LogicalPath.SelectSingle(message, "msg.PID.patientName[0].givenName")?.Value.ShouldBe("Jane");
        Hl7v2LogicalPath.SelectSingle(message, "msg.PID.dateTimeOfBirth")?.Value.ShouldBe("19800101");
        Hl7v2LogicalPath.SelectSingle(message, "msg.PID.administrativeSex")?.Value.ShouldBe("F");
    }

    [Fact]
    public void GivenAdtA01Er7_WhenProjectingToLogicalTree_ThenPreservesRepeatedIdentifiers()
    {
        var projection = new AdtA01Projection();
        var messageText = AdtA01.Replace(
            "12345^^^MRN^MR",
            "12345^^^MRN^MR~98765^^^SSN^SS",
            StringComparison.Ordinal);

        var message = projection.Project(messageText);
        var patientIdentifierList = Hl7v2LogicalPath.SelectSingle(message, "msg.PID")!
            .Children("patientIdentifierList");

        patientIdentifierList.Count.ShouldBe(2);
        Hl7v2LogicalPath.SelectSingle(message, "msg.PID.patientIdentifierList[0].idNumber")?.Value.ShouldBe("12345");
        Hl7v2LogicalPath.SelectSingle(message, "msg.PID.patientIdentifierList[1].idNumber")?.Value.ShouldBe("98765");
    }
}
```

- [ ] **Step 2: Run test to verify it fails because projection type does not exist**

Run:

```powershell
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net9.0 --no-restore --filter AdtA01ProjectionTests --verbosity minimal
```

Expected: FAIL with `CS0234` or `CS0246` for `Ignixa.Hl7v2.Projection.Messages.AdtA01Projection`.

- [ ] **Step 3: Rename implementation and namespace**

Create `src\Core\Ignixa.Hl7v2\Projection\Messages\AdtA01Projection.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Hl7v2.LogicalModel;
using NHapi.Base;
using NHapi.Base.Parser;
using NHapi.Base.Util;

namespace Ignixa.Hl7v2.Projection.Messages;

public sealed class AdtA01Projection
{
    public Hl7v2Element Project(string er7)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(er7);

        var parser = new PipeParser();
        var message = parser.Parse(NormalizeEr7(er7));
        var terser = new Terser(message);

        var pid = new Hl7v2Element(
            "PID",
            "PID",
            children:
            [
                .. ReadPatientIdentifiers(terser),
                .. ReadPatientNames(terser),
                Value("dateTimeOfBirth", "TS", GetOptional(terser, "/PID-7")),
                Value("administrativeSex", "IS", GetOptional(terser, "/PID-8"))
            ],
            location: "msg.PID");

        return new Hl7v2Element(
            "msg",
            "Hl7v2AdtA01",
            children: [pid],
            location: "msg");
    }

    private static IEnumerable<IElement> ReadPatientIdentifiers(Terser terser)
    {
        for (var repetition = 0; ; repetition++)
        {
            var idNumber = GetOptional(terser, $"/PID-3({repetition})-1");
            if (string.IsNullOrEmpty(idNumber))
            {
                yield break;
            }

            var assigningAuthority = new Hl7v2Element(
                "assigningAuthority",
                "HD",
                children:
                [
                    Value("namespaceId", "IS", GetOptional(terser, $"/PID-3({repetition})-4-1"))
                ],
                location: $"msg.PID.patientIdentifierList[{repetition}].assigningAuthority");

            yield return new Hl7v2Element(
                "patientIdentifierList",
                "CX",
                children:
                [
                    Value("idNumber", "ST", idNumber),
                    assigningAuthority
                ],
                location: $"msg.PID.patientIdentifierList[{repetition}]");
        }
    }

    private static IEnumerable<IElement> ReadPatientNames(Terser terser)
    {
        for (var repetition = 0; ; repetition++)
        {
            var familyName = GetOptional(terser, $"/PID-5({repetition})-1-1");
            var givenName = GetOptional(terser, $"/PID-5({repetition})-2");
            if (string.IsNullOrEmpty(familyName) && string.IsNullOrEmpty(givenName))
            {
                yield break;
            }

            var family = new Hl7v2Element(
                "familyName",
                "FN",
                children:
                [
                    Value("surname", "ST", familyName)
                ],
                location: $"msg.PID.patientName[{repetition}].familyName");

            yield return new Hl7v2Element(
                "patientName",
                "XPN",
                children:
                [
                    family,
                    Value("givenName", "ST", givenName)
                ],
                location: $"msg.PID.patientName[{repetition}]");
        }
    }

    private static Hl7v2Element Value(string name, string instanceType, string? value)
    {
        return new Hl7v2Element(name, instanceType, value);
    }

    private static string? GetOptional(Terser terser, string path)
    {
        try
        {
            return terser.Get(path);
        }
        catch (HL7Exception)
        {
            return null;
        }
    }

    private static string NormalizeEr7(string er7)
    {
        return er7
            .Replace("\r\n", "\r", StringComparison.Ordinal)
            .Replace('\n', '\r')
            .Trim();
    }
}
```

Delete `src\Core\Ignixa.Hl7v2\Adapters\NhapiAdtA01LogicalModelAdapter.cs`.

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net9.0 --no-restore --filter AdtA01ProjectionTests --verbosity minimal
```

Expected: PASS, 2 tests passed.

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Hl7v2 test\Ignixa.Hl7v2.Tests
git commit -m "Rename HL7v2 adapter prototype to projection" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 2: Add diagnostics and parse result types

**Files:**
- Create: `src\Core\Ignixa.Hl7v2\Validation\Hl7v2DiagnosticSeverity.cs`
- Create: `src\Core\Ignixa.Hl7v2\Validation\Hl7v2Diagnostic.cs`
- Create: `src\Core\Ignixa.Hl7v2\Parsing\Hl7v2ParseResult.cs`
- Test: `test\Ignixa.Hl7v2.Tests\Parsing\Hl7v2ParseResultTests.cs`

- [ ] **Step 1: Write the failing test**

Create `test\Ignixa.Hl7v2.Tests\Parsing\Hl7v2ParseResultTests.cs`:

```csharp
using Shouldly;
using Ignixa.Hl7v2.Parsing;
using Ignixa.Hl7v2.Validation;
using Xunit;

namespace Ignixa.Hl7v2.Tests.Parsing;

public class Hl7v2ParseResultTests
{
    [Fact]
    public void GivenSuccessResult_WhenInspecting_ThenHasNoDiagnostics()
    {
        var result = Hl7v2ParseResult.Success("ADT", "A01", "2.5", "MSG00001");

        result.IsSuccess.ShouldBeTrue();
        result.MessageCode.ShouldBe("ADT");
        result.TriggerEvent.ShouldBe("A01");
        result.Version.ShouldBe("2.5");
        result.MessageControlId.ShouldBe("MSG00001");
        result.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void GivenFailureResult_WhenInspecting_ThenContainsErrorDiagnostic()
    {
        var result = Hl7v2ParseResult.Failure(
            new Hl7v2Diagnostic(
                Hl7v2DiagnosticSeverity.Error,
                "HL7_PARSE_ERROR",
                "Invalid HL7v2 message"));

        result.IsSuccess.ShouldBeFalse();
        result.Diagnostics.Count.ShouldBe(1);
        result.Diagnostics[0].Code.ShouldBe("HL7_PARSE_ERROR");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net9.0 --no-restore --filter Hl7v2ParseResultTests --verbosity minimal
```

Expected: FAIL with missing `Hl7v2ParseResult` and diagnostic types.

- [ ] **Step 3: Add diagnostic and result types**

Create `src\Core\Ignixa.Hl7v2\Validation\Hl7v2DiagnosticSeverity.cs`:

```csharp
namespace Ignixa.Hl7v2.Validation;

public enum Hl7v2DiagnosticSeverity
{
    Information,
    Warning,
    Error
}
```

Create `src\Core\Ignixa.Hl7v2\Validation\Hl7v2Diagnostic.cs`:

```csharp
namespace Ignixa.Hl7v2.Validation;

public sealed record Hl7v2Diagnostic(
    Hl7v2DiagnosticSeverity Severity,
    string Code,
    string Message,
    string? Location = null,
    Exception? Exception = null);
```

Create `src\Core\Ignixa.Hl7v2\Parsing\Hl7v2ParseResult.cs`:

```csharp
using Ignixa.Hl7v2.Validation;

namespace Ignixa.Hl7v2.Parsing;

public sealed record Hl7v2ParseResult(
    bool IsSuccess,
    string? MessageCode,
    string? TriggerEvent,
    string? Version,
    string? MessageControlId,
    IReadOnlyList<Hl7v2Diagnostic> Diagnostics)
{
    public static Hl7v2ParseResult Success(
        string? messageCode,
        string? triggerEvent,
        string? version,
        string? messageControlId) =>
        new(true, messageCode, triggerEvent, version, messageControlId, []);

    public static Hl7v2ParseResult Failure(params Hl7v2Diagnostic[] diagnostics) =>
        new(false, null, null, null, null, diagnostics);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net9.0 --no-restore --filter Hl7v2ParseResultTests --verbosity minimal
```

Expected: PASS, 2 tests passed.

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Hl7v2\Validation src\Core\Ignixa.Hl7v2\Parsing test\Ignixa.Hl7v2.Tests\Parsing
git commit -m "Add HL7v2 parse diagnostics" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 3: Add a parser boundary that extracts MSH routing metadata

**Files:**
- Create: `src\Core\Ignixa.Hl7v2\Parsing\IHl7v2Parser.cs`
- Create: `src\Core\Ignixa.Hl7v2\Parsing\NhapiHl7v2Parser.cs`
- Test: `test\Ignixa.Hl7v2.Tests\Parsing\NhapiHl7v2ParserTests.cs`

- [ ] **Step 1: Write the failing test**

Create `test\Ignixa.Hl7v2.Tests\Parsing\NhapiHl7v2ParserTests.cs`:

```csharp
using Shouldly;
using Ignixa.Hl7v2.Parsing;
using Xunit;

namespace Ignixa.Hl7v2.Tests.Parsing;

public class NhapiHl7v2ParserTests
{
    [Fact]
    public void GivenAdtA01Er7_WhenParsing_ThenReturnsRoutingMetadata()
    {
        const string message = """
            MSH|^~\&|SendingApp|SendingFacility|ReceivingApp|ReceivingFacility|202607091200||ADT^A01|MSG00001|P|2.5
            EVN|A01|202607091200
            PID|1||12345^^^MRN^MR||Doe^Jane^A||19800101|F
            """;

        IHl7v2Parser parser = new NhapiHl7v2Parser();

        var result = parser.Parse(message);

        result.IsSuccess.ShouldBeTrue();
        result.MessageCode.ShouldBe("ADT");
        result.TriggerEvent.ShouldBe("A01");
        result.Version.ShouldBe("2.5");
        result.MessageControlId.ShouldBe("MSG00001");
    }

    [Fact]
    public void GivenInvalidEr7_WhenParsing_ThenReturnsDiagnostic()
    {
        IHl7v2Parser parser = new NhapiHl7v2Parser();

        var result = parser.Parse("not an HL7 message");

        result.IsSuccess.ShouldBeFalse();
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "HL7_PARSE_ERROR");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net9.0 --no-restore --filter NhapiHl7v2ParserTests --verbosity minimal
```

Expected: FAIL with missing `IHl7v2Parser` and `NhapiHl7v2Parser`.

- [ ] **Step 3: Implement parser boundary**

Create `src\Core\Ignixa.Hl7v2\Parsing\IHl7v2Parser.cs`:

```csharp
namespace Ignixa.Hl7v2.Parsing;

public interface IHl7v2Parser
{
    Hl7v2ParseResult Parse(string er7);
}
```

Create `src\Core\Ignixa.Hl7v2\Parsing\NhapiHl7v2Parser.cs`:

```csharp
using Ignixa.Hl7v2.Validation;
using NHapi.Base;
using NHapi.Base.Parser;
using NHapi.Base.Util;

namespace Ignixa.Hl7v2.Parsing;

public sealed class NhapiHl7v2Parser : IHl7v2Parser
{
    public Hl7v2ParseResult Parse(string er7)
    {
        if (string.IsNullOrWhiteSpace(er7))
        {
            return Hl7v2ParseResult.Failure(new Hl7v2Diagnostic(
                Hl7v2DiagnosticSeverity.Error,
                "HL7_PARSE_ERROR",
                "HL7v2 message cannot be empty"));
        }

        try
        {
            var parser = new PipeParser();
            var message = parser.Parse(NormalizeEr7(er7));
            var terser = new Terser(message);

            return Hl7v2ParseResult.Success(
                terser.Get("/MSH-9-1"),
                terser.Get("/MSH-9-2"),
                terser.Get("/MSH-12"),
                terser.Get("/MSH-10"));
        }
        catch (HL7Exception ex)
        {
            return Hl7v2ParseResult.Failure(new Hl7v2Diagnostic(
                Hl7v2DiagnosticSeverity.Error,
                "HL7_PARSE_ERROR",
                ex.Message,
                Exception: ex));
        }
    }

    private static string NormalizeEr7(string er7)
    {
        return er7
            .Replace("\r\n", "\r", StringComparison.Ordinal)
            .Replace('\n', '\r')
            .Trim();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net9.0 --no-restore --filter NhapiHl7v2ParserTests --verbosity minimal
```

Expected: PASS, 2 tests passed.

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Hl7v2\Parsing test\Ignixa.Hl7v2.Tests\Parsing
git commit -m "Add NHapi HL7v2 parser boundary" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 4: Add projection context, result, and registry contracts

**Files:**
- Create: `src\Core\Ignixa.Hl7v2\Projection\Hl7v2ProjectionContext.cs`
- Create: `src\Core\Ignixa.Hl7v2\Projection\Hl7v2ProjectionResult.cs`
- Create: `src\Core\Ignixa.Hl7v2\Projection\IHl7v2Projection.cs`
- Create: `src\Core\Ignixa.Hl7v2\Projection\Hl7v2ProjectionRegistry.cs`
- Test: `test\Ignixa.Hl7v2.Tests\Projection\Hl7v2ProjectionRegistryTests.cs`

- [ ] **Step 1: Write the failing test**

Create `test\Ignixa.Hl7v2.Tests\Projection\Hl7v2ProjectionRegistryTests.cs`:

```csharp
using Shouldly;
using Ignixa.Hl7v2.LogicalModel;
using Ignixa.Hl7v2.Projection;
using Xunit;

namespace Ignixa.Hl7v2.Tests.Projection;

public class Hl7v2ProjectionRegistryTests
{
    [Fact]
    public void GivenRegisteredProjection_WhenResolvingMatchingContext_ThenReturnsProjection()
    {
        var projection = new TestProjection("ADT", "A01", "2.5");
        var registry = new Hl7v2ProjectionRegistry([projection]);
        var context = new Hl7v2ProjectionContext("ADT", "A01", "2.5", "MSG00001", "raw");

        var resolved = registry.Resolve(context);

        resolved.ShouldBeSameAs(projection);
    }

    [Fact]
    public void GivenNoRegisteredProjection_WhenResolving_ThenThrowsClearException()
    {
        var registry = new Hl7v2ProjectionRegistry([]);
        var context = new Hl7v2ProjectionContext("ORU", "R01", "2.5", "MSG00002", "raw");

        var exception = Should.Throw<InvalidOperationException>(() => registry.Resolve(context));

        exception.Message.ShouldContain("ORU^R01");
        exception.Message.ShouldContain("2.5");
    }

    private sealed class TestProjection(
        string messageCode,
        string triggerEvent,
        string version) : IHl7v2Projection
    {
        public bool CanProject(Hl7v2ProjectionContext context)
        {
            return context.MessageCode == messageCode
                && context.TriggerEvent == triggerEvent
                && context.Version == version;
        }

        public Hl7v2ProjectionResult Project(Hl7v2ProjectionContext context)
        {
            return Hl7v2ProjectionResult.Success(new Hl7v2Element("msg", "TestMessage"));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net9.0 --no-restore --filter Hl7v2ProjectionRegistryTests --verbosity minimal
```

Expected: FAIL with missing projection contract types.

- [ ] **Step 3: Add projection contract and registry**

Create `src\Core\Ignixa.Hl7v2\Projection\Hl7v2ProjectionContext.cs`:

```csharp
namespace Ignixa.Hl7v2.Projection;

public sealed record Hl7v2ProjectionContext(
    string? MessageCode,
    string? TriggerEvent,
    string? Version,
    string? MessageControlId,
    string RawMessage);
```

Create `src\Core\Ignixa.Hl7v2\Projection\Hl7v2ProjectionResult.cs`:

```csharp
using Ignixa.Abstractions;
using Ignixa.Hl7v2.Validation;

namespace Ignixa.Hl7v2.Projection;

public sealed record Hl7v2ProjectionResult(
    bool IsSuccess,
    IElement? Root,
    IReadOnlyList<Hl7v2Diagnostic> Diagnostics)
{
    public static Hl7v2ProjectionResult Success(IElement root) =>
        new(true, root, []);

    public static Hl7v2ProjectionResult Failure(params Hl7v2Diagnostic[] diagnostics) =>
        new(false, null, diagnostics);
}
```

Create `src\Core\Ignixa.Hl7v2\Projection\IHl7v2Projection.cs`:

```csharp
namespace Ignixa.Hl7v2.Projection;

public interface IHl7v2Projection
{
    bool CanProject(Hl7v2ProjectionContext context);

    Hl7v2ProjectionResult Project(Hl7v2ProjectionContext context);
}
```

Create `src\Core\Ignixa.Hl7v2\Projection\Hl7v2ProjectionRegistry.cs`:

```csharp
namespace Ignixa.Hl7v2.Projection;

public sealed class Hl7v2ProjectionRegistry
{
    private readonly IReadOnlyList<IHl7v2Projection> _projections;

    public Hl7v2ProjectionRegistry(IEnumerable<IHl7v2Projection> projections)
    {
        ArgumentNullException.ThrowIfNull(projections);
        _projections = projections.ToList();
    }

    public IHl7v2Projection Resolve(Hl7v2ProjectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var projection = _projections.FirstOrDefault(candidate => candidate.CanProject(context));
        if (projection is not null)
        {
            return projection;
        }

        throw new InvalidOperationException(
            $"No HL7v2 projection registered for {context.MessageCode}^{context.TriggerEvent} version {context.Version}");
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net9.0 --no-restore --filter Hl7v2ProjectionRegistryTests --verbosity minimal
```

Expected: PASS, 2 tests passed.

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Hl7v2\Projection test\Ignixa.Hl7v2.Tests\Projection
git commit -m "Add HL7v2 projection registry" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 5: Split ADT_A01 into message, segment, and datatype projections

**Files:**
- Modify: `src\Core\Ignixa.Hl7v2\Projection\Messages\AdtA01Projection.cs`
- Create: `src\Core\Ignixa.Hl7v2\Projection\Segments\PidProjection.cs`
- Create: `src\Core\Ignixa.Hl7v2\Projection\Datatypes\CxProjection.cs`
- Create: `src\Core\Ignixa.Hl7v2\Projection\Datatypes\HdProjection.cs`
- Create: `src\Core\Ignixa.Hl7v2\Projection\Datatypes\XpnProjection.cs`
- Create: `src\Core\Ignixa.Hl7v2\Projection\Datatypes\FnProjection.cs`
- Test: `test\Ignixa.Hl7v2.Tests\Projection\AdtA01ProjectionTests.cs`

- [ ] **Step 1: Extend tests to prove structure remains stable after split**

Add this test to `test\Ignixa.Hl7v2.Tests\Projection\AdtA01ProjectionTests.cs`:

```csharp
[Fact]
public void GivenAdtA01Projection_WhenProjecting_ThenUsesStablePidDatatypePaths()
{
    var projection = new AdtA01Projection();
    var context = new Hl7v2ProjectionContext("ADT", "A01", "2.5", "MSG00001", AdtA01);

    var result = projection.Project(context);

    result.IsSuccess.ShouldBeTrue();
    var root = result.Root.ShouldNotBeNull();
    Hl7v2LogicalPath.SelectSingle(root, "msg.PID.patientIdentifierList[0].idNumber")?.Value.ShouldBe("12345");
    Hl7v2LogicalPath.SelectSingle(root, "msg.PID.patientIdentifierList[0].assigningAuthority.namespaceId")?.Value.ShouldBe("MRN");
    Hl7v2LogicalPath.SelectSingle(root, "msg.PID.patientName[0].familyName.surname")?.Value.ShouldBe("Doe");
    Hl7v2LogicalPath.SelectSingle(root, "msg.PID.patientName[0].givenName")?.Value.ShouldBe("Jane");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net9.0 --no-restore --filter AdtA01ProjectionTests --verbosity minimal
```

Expected: FAIL because `AdtA01Projection.Project(Hl7v2ProjectionContext)` is not implemented yet.

- [ ] **Step 3: Add datatype and segment projection classes**

Create `src\Core\Ignixa.Hl7v2\Projection\Datatypes\HdProjection.cs`:

```csharp
using Ignixa.Abstractions;
using Ignixa.Hl7v2.LogicalModel;
using NHapi.Base.Util;

namespace Ignixa.Hl7v2.Projection.Datatypes;

public sealed class HdProjection
{
    public IElement ProjectAssigningAuthority(Terser terser, string sourcePath, string location)
    {
        return new Hl7v2Element(
            "assigningAuthority",
            "HD",
            children:
            [
                new Hl7v2Element("namespaceId", "IS", GetOptional(terser, $"{sourcePath}-1"))
            ],
            location: location);
    }

    private static string? GetOptional(Terser terser, string path)
    {
        try
        {
            return terser.Get(path);
        }
        catch (NHapi.Base.HL7Exception)
        {
            return null;
        }
    }
}
```

Create `src\Core\Ignixa.Hl7v2\Projection\Datatypes\CxProjection.cs`:

```csharp
using Ignixa.Abstractions;
using Ignixa.Hl7v2.LogicalModel;
using NHapi.Base.Util;

namespace Ignixa.Hl7v2.Projection.Datatypes;

public sealed class CxProjection(HdProjection hdProjection)
{
    public IElement Project(Terser terser, string sourcePath, string location)
    {
        return new Hl7v2Element(
            "patientIdentifierList",
            "CX",
            children:
            [
                new Hl7v2Element("idNumber", "ST", GetOptional(terser, $"{sourcePath}-1")),
                hdProjection.ProjectAssigningAuthority(terser, $"{sourcePath}-4", $"{location}.assigningAuthority")
            ],
            location: location);
    }

    private static string? GetOptional(Terser terser, string path)
    {
        try
        {
            return terser.Get(path);
        }
        catch (NHapi.Base.HL7Exception)
        {
            return null;
        }
    }
}
```

Create `src\Core\Ignixa.Hl7v2\Projection\Datatypes\FnProjection.cs`:

```csharp
using Ignixa.Abstractions;
using Ignixa.Hl7v2.LogicalModel;
using NHapi.Base.Util;

namespace Ignixa.Hl7v2.Projection.Datatypes;

public sealed class FnProjection
{
    public IElement Project(Terser terser, string sourcePath, string location)
    {
        return new Hl7v2Element(
            "familyName",
            "FN",
            children:
            [
                new Hl7v2Element("surname", "ST", GetOptional(terser, $"{sourcePath}-1"))
            ],
            location: location);
    }

    private static string? GetOptional(Terser terser, string path)
    {
        try
        {
            return terser.Get(path);
        }
        catch (NHapi.Base.HL7Exception)
        {
            return null;
        }
    }
}
```

Create `src\Core\Ignixa.Hl7v2\Projection\Datatypes\XpnProjection.cs`:

```csharp
using Ignixa.Abstractions;
using Ignixa.Hl7v2.LogicalModel;
using NHapi.Base.Util;

namespace Ignixa.Hl7v2.Projection.Datatypes;

public sealed class XpnProjection(FnProjection fnProjection)
{
    public IElement Project(Terser terser, string sourcePath, string location)
    {
        return new Hl7v2Element(
            "patientName",
            "XPN",
            children:
            [
                fnProjection.Project(terser, $"{sourcePath}-1", $"{location}.familyName"),
                new Hl7v2Element("givenName", "ST", GetOptional(terser, $"{sourcePath}-2"))
            ],
            location: location);
    }

    private static string? GetOptional(Terser terser, string path)
    {
        try
        {
            return terser.Get(path);
        }
        catch (NHapi.Base.HL7Exception)
        {
            return null;
        }
    }
}
```

Create `src\Core\Ignixa.Hl7v2\Projection\Segments\PidProjection.cs`:

```csharp
using Ignixa.Abstractions;
using Ignixa.Hl7v2.LogicalModel;
using Ignixa.Hl7v2.Projection.Datatypes;
using NHapi.Base.Util;

namespace Ignixa.Hl7v2.Projection.Segments;

public sealed class PidProjection(
    CxProjection cxProjection,
    XpnProjection xpnProjection)
{
    public IElement Project(Terser terser)
    {
        return new Hl7v2Element(
            "PID",
            "PID",
            children:
            [
                .. ReadPatientIdentifiers(terser),
                .. ReadPatientNames(terser),
                new Hl7v2Element("dateTimeOfBirth", "TS", GetOptional(terser, "/PID-7")),
                new Hl7v2Element("administrativeSex", "IS", GetOptional(terser, "/PID-8"))
            ],
            location: "msg.PID");
    }

    private IEnumerable<IElement> ReadPatientIdentifiers(Terser terser)
    {
        for (var repetition = 0; ; repetition++)
        {
            var idNumber = GetOptional(terser, $"/PID-3({repetition})-1");
            if (string.IsNullOrEmpty(idNumber))
            {
                yield break;
            }

            yield return cxProjection.Project(
                terser,
                $"/PID-3({repetition})",
                $"msg.PID.patientIdentifierList[{repetition}]");
        }
    }

    private IEnumerable<IElement> ReadPatientNames(Terser terser)
    {
        for (var repetition = 0; ; repetition++)
        {
            var familyName = GetOptional(terser, $"/PID-5({repetition})-1-1");
            var givenName = GetOptional(terser, $"/PID-5({repetition})-2");
            if (string.IsNullOrEmpty(familyName) && string.IsNullOrEmpty(givenName))
            {
                yield break;
            }

            yield return xpnProjection.Project(
                terser,
                $"/PID-5({repetition})",
                $"msg.PID.patientName[{repetition}]");
        }
    }

    private static string? GetOptional(Terser terser, string path)
    {
        try
        {
            return terser.Get(path);
        }
        catch (NHapi.Base.HL7Exception)
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Update `AdtA01Projection` to implement `IHl7v2Projection`**

Replace `src\Core\Ignixa.Hl7v2\Projection\Messages\AdtA01Projection.cs` with:

```csharp
using Ignixa.Hl7v2.LogicalModel;
using Ignixa.Hl7v2.Projection.Datatypes;
using Ignixa.Hl7v2.Projection.Segments;
using Ignixa.Hl7v2.Validation;
using NHapi.Base;
using NHapi.Base.Parser;
using NHapi.Base.Util;

namespace Ignixa.Hl7v2.Projection.Messages;

public sealed class AdtA01Projection : IHl7v2Projection
{
    private readonly PidProjection _pidProjection = new(
        new CxProjection(new HdProjection()),
        new XpnProjection(new FnProjection()));

    public bool CanProject(Hl7v2ProjectionContext context)
    {
        return string.Equals(context.MessageCode, "ADT", StringComparison.Ordinal)
            && string.Equals(context.TriggerEvent, "A01", StringComparison.Ordinal);
    }

    public Hl7v2ProjectionResult Project(Hl7v2ProjectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            var parser = new PipeParser();
            var message = parser.Parse(NormalizeEr7(context.RawMessage));
            var terser = new Terser(message);

            return Hl7v2ProjectionResult.Success(new Hl7v2Element(
                "msg",
                "Hl7v2AdtA01",
                children: [_pidProjection.Project(terser)],
                location: "msg"));
        }
        catch (HL7Exception ex)
        {
            return Hl7v2ProjectionResult.Failure(new Hl7v2Diagnostic(
                Hl7v2DiagnosticSeverity.Error,
                "HL7_PROJECTION_ERROR",
                ex.Message,
                Exception: ex));
        }
    }

    public Hl7v2Element Project(string er7)
    {
        var context = new Hl7v2ProjectionContext("ADT", "A01", null, null, er7);
        var result = Project(context);
        if (result.Root is Hl7v2Element root)
        {
            return root;
        }

        throw new InvalidOperationException(result.Diagnostics.FirstOrDefault()?.Message ?? "HL7v2 projection failed");
    }

    private static string NormalizeEr7(string er7)
    {
        return er7
            .Replace("\r\n", "\r", StringComparison.Ordinal)
            .Replace('\n', '\r')
            .Trim();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run:

```powershell
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net9.0 --no-restore --filter AdtA01ProjectionTests --verbosity minimal
```

Expected: PASS, all ADT_A01 projection tests pass.

- [ ] **Step 6: Commit**

```powershell
git add src\Core\Ignixa.Hl7v2\Projection test\Ignixa.Hl7v2.Tests\Projection
git commit -m "Split ADT projection by segment and datatype" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 6: Add custom Z-segment projection extension point

**Files:**
- Create: `src\Core\Ignixa.Hl7v2\Projection\Segments\ZSegmentProjection.cs`
- Test: `test\Ignixa.Hl7v2.Tests\Projection\CustomSegmentProjectionTests.cs`

- [ ] **Step 1: Write the failing test**

Create `test\Ignixa.Hl7v2.Tests\Projection\CustomSegmentProjectionTests.cs`:

```csharp
using Shouldly;
using Ignixa.Hl7v2.LogicalModel;
using Ignixa.Hl7v2.Projection.Segments;
using Xunit;

namespace Ignixa.Hl7v2.Tests.Projection;

public class CustomSegmentProjectionTests
{
    [Fact]
    public void GivenConfiguredZSegment_WhenProjecting_ThenExposesConfiguredFields()
    {
        var projection = new ZSegmentProjection(
            "ZPD",
            [
                new ZSegmentFieldDefinition(1, "favoriteColor", "ST"),
                new ZSegmentFieldDefinition(2, "localRiskCode", "CWE")
            ]);

        var segment = projection.Project("ZPD|blue|R42");

        segment.Name.ShouldBe("ZPD");
        Hl7v2LogicalPath.SelectSingle(segment, "ZPD.favoriteColor")?.Value.ShouldBe("blue");
        Hl7v2LogicalPath.SelectSingle(segment, "ZPD.localRiskCode")?.Value.ShouldBe("R42");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net9.0 --no-restore --filter CustomSegmentProjectionTests --verbosity minimal
```

Expected: FAIL with missing `ZSegmentProjection` and `ZSegmentFieldDefinition`.

- [ ] **Step 3: Add Z-segment projection**

Create `src\Core\Ignixa.Hl7v2\Projection\Segments\ZSegmentProjection.cs`:

```csharp
using Ignixa.Hl7v2.LogicalModel;

namespace Ignixa.Hl7v2.Projection.Segments;

public sealed record ZSegmentFieldDefinition(
    int FieldNumber,
    string Name,
    string InstanceType);

public sealed class ZSegmentProjection(
    string segmentName,
    IReadOnlyList<ZSegmentFieldDefinition> fields)
{
    public Hl7v2Element Project(string segmentText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentText);

        var parts = segmentText.Split('|');
        if (!string.Equals(parts[0], segmentName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Expected segment '{segmentName}' but received '{parts[0]}'",
                nameof(segmentText));
        }

        var children = fields
            .Where(field => field.FieldNumber < parts.Length)
            .Select(field => new Hl7v2Element(
                field.Name,
                field.InstanceType,
                parts[field.FieldNumber],
                location: $"{segmentName}.{field.Name}"))
            .ToList();

        return new Hl7v2Element(segmentName, segmentName, children: children, location: segmentName);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net9.0 --no-restore --filter CustomSegmentProjectionTests --verbosity minimal
```

Expected: PASS, 1 test passed.

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Hl7v2\Projection\Segments\ZSegmentProjection.cs test\Ignixa.Hl7v2.Tests\Projection\CustomSegmentProjectionTests.cs
git commit -m "Add configurable Z-segment projection" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 7: Add outbound encoding boundary

**Files:**
- Create: `src\Core\Ignixa.Hl7v2\Encoding\Hl7v2EncodeResult.cs`
- Create: `src\Core\Ignixa.Hl7v2\Encoding\IHl7v2Encoder.cs`
- Create: `src\Core\Ignixa.Hl7v2\Encoding\NhapiHl7v2Encoder.cs`
- Test: `test\Ignixa.Hl7v2.Tests\Encoding\NhapiHl7v2EncoderTests.cs`

- [ ] **Step 1: Write the failing test**

Create `test\Ignixa.Hl7v2.Tests\Encoding\NhapiHl7v2EncoderTests.cs`:

```csharp
using Shouldly;
using Ignixa.Hl7v2.Encoding;
using Ignixa.Hl7v2.LogicalModel;
using Xunit;

namespace Ignixa.Hl7v2.Tests.Encoding;

public class NhapiHl7v2EncoderTests
{
    [Fact]
    public void GivenMinimalAdtA08LogicalTree_WhenEncoding_ThenProducesEr7WithMshAndPid()
    {
        var message = new Hl7v2Element(
            "msg",
            "Hl7v2AdtA08",
            children:
            [
                new Hl7v2Element(
                    "MSH",
                    "MSH",
                    children:
                    [
                        new Hl7v2Element("messageControlId", "ST", "MSG00002"),
                        new Hl7v2Element("versionId", "VID", "2.5")
                    ]),
                new Hl7v2Element(
                    "PID",
                    "PID",
                    children:
                    [
                        new Hl7v2Element(
                            "patientIdentifierList",
                            "CX",
                            children: [new Hl7v2Element("idNumber", "ST", "12345")]),
                        new Hl7v2Element(
                            "patientName",
                            "XPN",
                            children:
                            [
                                new Hl7v2Element(
                                    "familyName",
                                    "FN",
                                    children: [new Hl7v2Element("surname", "ST", "Doe")]),
                                new Hl7v2Element("givenName", "ST", "Jane")
                            ]),
                        new Hl7v2Element("dateTimeOfBirth", "TS", "19800101"),
                        new Hl7v2Element("administrativeSex", "IS", "F")
                    ])
            ]);

        IHl7v2Encoder encoder = new NhapiHl7v2Encoder();

        var result = encoder.Encode(message);

        result.IsSuccess.ShouldBeTrue();
        result.Er7.ShouldContain("MSH|^~\\&|");
        result.Er7.ShouldContain("ADT^A08");
        result.Er7.ShouldContain("PID|1||12345");
        result.Er7.ShouldContain("Doe^Jane");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net9.0 --no-restore --filter NhapiHl7v2EncoderTests --verbosity minimal
```

Expected: FAIL with missing encoding types.

- [ ] **Step 3: Add encoder boundary**

Create `src\Core\Ignixa.Hl7v2\Encoding\Hl7v2EncodeResult.cs`:

```csharp
using Ignixa.Hl7v2.Validation;

namespace Ignixa.Hl7v2.Encoding;

public sealed record Hl7v2EncodeResult(
    bool IsSuccess,
    string? Er7,
    IReadOnlyList<Hl7v2Diagnostic> Diagnostics)
{
    public static Hl7v2EncodeResult Success(string er7) => new(true, er7, []);

    public static Hl7v2EncodeResult Failure(params Hl7v2Diagnostic[] diagnostics) => new(false, null, diagnostics);
}
```

Create `src\Core\Ignixa.Hl7v2\Encoding\IHl7v2Encoder.cs`:

```csharp
using Ignixa.Abstractions;

namespace Ignixa.Hl7v2.Encoding;

public interface IHl7v2Encoder
{
    Hl7v2EncodeResult Encode(IElement root);
}
```

Create `src\Core\Ignixa.Hl7v2\Encoding\NhapiHl7v2Encoder.cs`:

```csharp
using Ignixa.Abstractions;
using Ignixa.Hl7v2.LogicalModel;
using Ignixa.Hl7v2.Validation;

namespace Ignixa.Hl7v2.Encoding;

public sealed class NhapiHl7v2Encoder : IHl7v2Encoder
{
    public Hl7v2EncodeResult Encode(IElement root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (!string.Equals(root.InstanceType, "Hl7v2AdtA08", StringComparison.Ordinal))
        {
            return Hl7v2EncodeResult.Failure(new Hl7v2Diagnostic(
                Hl7v2DiagnosticSeverity.Error,
                "HL7_ENCODE_UNSUPPORTED_MESSAGE",
                $"Unsupported outbound message type '{root.InstanceType}'"));
        }

        var messageControlId = Hl7v2LogicalPath.SelectSingle(root, "msg.MSH.messageControlId")?.Value?.ToString() ?? "MSG00001";
        var version = Hl7v2LogicalPath.SelectSingle(root, "msg.MSH.versionId")?.Value?.ToString() ?? "2.5";
        var idNumber = Hl7v2LogicalPath.SelectSingle(root, "msg.PID.patientIdentifierList[0].idNumber")?.Value?.ToString() ?? "";
        var family = Hl7v2LogicalPath.SelectSingle(root, "msg.PID.patientName[0].familyName.surname")?.Value?.ToString() ?? "";
        var given = Hl7v2LogicalPath.SelectSingle(root, "msg.PID.patientName[0].givenName")?.Value?.ToString() ?? "";
        var birthDate = Hl7v2LogicalPath.SelectSingle(root, "msg.PID.dateTimeOfBirth")?.Value?.ToString() ?? "";
        var administrativeSex = Hl7v2LogicalPath.SelectSingle(root, "msg.PID.administrativeSex")?.Value?.ToString() ?? "";

        var er7 = string.Join(
            "\r",
            $"MSH|^~\\&|||||{DateTime.UtcNow:yyyyMMddHHmmss}||ADT^A08|{messageControlId}|P|{version}",
            $"PID|1||{idNumber}||{family}^{given}||{birthDate}|{administrativeSex}");

        return Hl7v2EncodeResult.Success(er7);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net9.0 --no-restore --filter NhapiHl7v2EncoderTests --verbosity minimal
```

Expected: PASS, 1 test passed.

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Hl7v2\Encoding test\Ignixa.Hl7v2.Tests\Encoding
git commit -m "Add HL7v2 outbound encoding boundary" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 8: Add ACK/NACK builder for parse and projection failures

**Files:**
- Create: `src\Core\Ignixa.Hl7v2\Acknowledgments\Hl7v2AckBuilder.cs`
- Test: `test\Ignixa.Hl7v2.Tests\Acknowledgments\Hl7v2AckBuilderTests.cs`

- [ ] **Step 1: Write the failing test**

Create `test\Ignixa.Hl7v2.Tests\Acknowledgments\Hl7v2AckBuilderTests.cs`:

```csharp
using Shouldly;
using Ignixa.Hl7v2.Acknowledgments;
using Ignixa.Hl7v2.Validation;
using Xunit;

namespace Ignixa.Hl7v2.Tests.Acknowledgments;

public class Hl7v2AckBuilderTests
{
    [Fact]
    public void GivenAcceptedMessage_WhenBuildingAck_ThenCreatesApplicationAcceptAck()
    {
        var builder = new Hl7v2AckBuilder();

        var ack = builder.BuildApplicationAccept("MSG00001", "2.5");

        ack.ShouldContain("MSH|^~\\&|");
        ack.ShouldContain("ACK");
        ack.ShouldContain("MSA|AA|MSG00001");
    }

    [Fact]
    public void GivenErrorDiagnostic_WhenBuildingNack_ThenCreatesApplicationErrorAck()
    {
        var builder = new Hl7v2AckBuilder();
        var diagnostic = new Hl7v2Diagnostic(
            Hl7v2DiagnosticSeverity.Error,
            "HL7_PARSE_ERROR",
            "Invalid message");

        var ack = builder.BuildApplicationError("MSG00001", "2.5", diagnostic);

        ack.ShouldContain("MSA|AE|MSG00001");
        ack.ShouldContain("ERR|||HL7_PARSE_ERROR");
        ack.ShouldContain("Invalid message");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net9.0 --no-restore --filter Hl7v2AckBuilderTests --verbosity minimal
```

Expected: FAIL with missing `Hl7v2AckBuilder`.

- [ ] **Step 3: Add ACK builder**

Create `src\Core\Ignixa.Hl7v2\Acknowledgments\Hl7v2AckBuilder.cs`:

```csharp
using Ignixa.Hl7v2.Validation;

namespace Ignixa.Hl7v2.Acknowledgments;

public sealed class Hl7v2AckBuilder
{
    public string BuildApplicationAccept(string? messageControlId, string? version)
    {
        var controlId = string.IsNullOrWhiteSpace(messageControlId) ? "UNKNOWN" : messageControlId;
        var hl7Version = string.IsNullOrWhiteSpace(version) ? "2.5" : version;

        return string.Join(
            "\r",
            $"MSH|^~\\&|||||{DateTime.UtcNow:yyyyMMddHHmmss}||ACK|ACK-{controlId}|P|{hl7Version}",
            $"MSA|AA|{controlId}");
    }

    public string BuildApplicationError(string? messageControlId, string? version, Hl7v2Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        var controlId = string.IsNullOrWhiteSpace(messageControlId) ? "UNKNOWN" : messageControlId;
        var hl7Version = string.IsNullOrWhiteSpace(version) ? "2.5" : version;

        return string.Join(
            "\r",
            $"MSH|^~\\&|||||{DateTime.UtcNow:yyyyMMddHHmmss}||ACK|ACK-{controlId}|P|{hl7Version}",
            $"MSA|AE|{controlId}",
            $"ERR|||{diagnostic.Code}|E|{Escape(diagnostic.Message)}");
    }

    private static string Escape(string value)
    {
        return value
            .Replace("\\", "\\E\\", StringComparison.Ordinal)
            .Replace("|", "\\F\\", StringComparison.Ordinal)
            .Replace("^", "\\S\\", StringComparison.Ordinal)
            .Replace("&", "\\T\\", StringComparison.Ordinal)
            .Replace("~", "\\R\\", StringComparison.Ordinal);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net9.0 --no-restore --filter Hl7v2AckBuilderTests --verbosity minimal
```

Expected: PASS, 2 tests passed.

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Hl7v2\Acknowledgments test\Ignixa.Hl7v2.Tests\Acknowledgments
git commit -m "Add HL7v2 ACK builder" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 9: Document projection architecture in feature investigation

**Files:**
- Modify: `docs\features\hl7v2-mapping\investigations\nhapi-logical-model-adapter.md`
- Test: no test file; verify with focused test project and git diff

- [ ] **Step 1: Update investigation with projection terminology**

Append this section to `docs\features\hl7v2-mapping\investigations\nhapi-logical-model-adapter.md`:

```markdown
### Projection architecture follow-up

The adapter prototype should evolve into projection architecture:

```text
IHl7v2Projection
  MessageProjections
    AdtA01Projection
    AdtA08Projection
    OruR01Projection
  SegmentProjections
    MshProjection
    PidProjection
    Pv1Projection
    ObrProjection
    ObxProjection
    ZSegmentProjection
  DatatypeProjections
    CxProjection
    HdProjection
    XpnProjection
    FnProjection
    CweProjection
    TsProjection
```

Projection code exposes HL7v2 structure, not clinical meaning. For example, `msg.PID.patientIdentifierList[0].assigningAuthority.namespaceId` is valid projection output. `msg.patient.mrn` is not valid projection output because it bakes site-specific semantics into the adapter.

Extension points:

1. Register custom `IHl7v2Projection` instances for tenant-specific message profiles.
2. Register `ZSegmentProjection` definitions for local Z-segments.
3. Add version-specific projection classes when the same logical path needs different NHapi extraction rules.
4. Keep outbound encoding behind `IHl7v2Encoder` so FML output can be validated before ER7 serialization.
```

- [ ] **Step 2: Verify docs and tests**

Run:

```powershell
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net9.0 --no-restore --verbosity minimal
```

Expected: PASS.

- [ ] **Step 3: Commit**

```powershell
git add docs\features\hl7v2-mapping\investigations\nhapi-logical-model-adapter.md
git commit -m "Document HL7v2 projection architecture" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 10: Final two-framework verification

**Files:**
- Verify all files changed in this plan

- [ ] **Step 1: Run HL7v2 tests on both target frameworks**

Run:

```powershell
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net9.0 --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet test test\Ignixa.Hl7v2.Tests\Ignixa.Hl7v2.Tests.csproj --framework net10.0 --no-restore --verbosity minimal
```

Expected: PASS on `net9.0` and `net10.0`.

- [ ] **Step 2: Confirm branch diff**

Run:

```powershell
git --no-pager status --short
git --no-pager log --oneline -10
```

Expected: only intentional files are modified or all plan commits are present with a clean working tree.

- [ ] **Step 3: Push branch**

Run:

```powershell
git push
```

Expected: branch pushes to `origin/brendankowitz-investigate-fhir-mapping-hl7v2`.

---

## Self-Review

**Spec coverage:** This plan covers the missing projection extensibility model: projection naming, projection registry, version/message routing metadata, segment/datatype split, custom Z-segment extension, outbound encoding boundary, ACK/NACK, diagnostics, documentation, and two-framework verification.

**Placeholder scan:** No task uses open-ended placeholder instructions. Each code task includes concrete test code, concrete implementation code, and exact commands.

**Type consistency:** The plan consistently uses `Projection` naming: `IHl7v2Projection`, `Hl7v2ProjectionContext`, `Hl7v2ProjectionRegistry`, `AdtA01Projection`, `PidProjection`, `CxProjection`, and `ZSegmentProjection`.

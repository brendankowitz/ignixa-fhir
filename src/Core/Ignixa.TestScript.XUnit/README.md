# Ignixa.TestScript.XUnit

xUnit integration for the TestScript execution engine. Discover and run FHIR TestScript files as xUnit theory test cases.

## Installation

```bash
dotnet add package Ignixa.TestScript.XUnit
```

## Usage

```csharp
using Ignixa.TestScript.XUnit;

public class ConformanceTests
{
    [Theory]
    [TestScriptData("testscripts/**/*.json")]
    public async Task ExecuteTestScript(string testScriptPath)
    {
        var result = TestScriptParser.ParseFile(testScriptPath);
        if (!result.IsSuccess)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Message)));
        var evaluator = CreateEvaluator();
        var report = await evaluator.ExecuteAsync(result.Value!, CancellationToken.None);
        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
    }
}
```

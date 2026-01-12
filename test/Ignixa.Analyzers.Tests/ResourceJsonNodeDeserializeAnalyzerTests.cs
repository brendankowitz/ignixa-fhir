using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;

namespace Ignixa.Analyzers.Tests;

public class ResourceJsonNodeDeserializeAnalyzerTests
{
    [Fact]
    public async Task GivenJsonSerializerDeserializeWithResourceJsonNode_ThenReportsDiagnostic()
    {
        var testCode = """
            using System.Text.Json;
            using Ignixa.Serialization.SourceNodes;

            class TestClass
            {
                void TestMethod()
                {
                    var json = "{}";
                    var resource = JsonSerializer.Deserialize<ResourceJsonNode>(json);
                }
            }
            """;

        var expected = new DiagnosticResult(ResourceJsonNodeDeserializeAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
            .WithSpan(9, 24, 9, 79)
            .WithArguments("ResourceJsonNode");

        await VerifyAnalyzerAsync(testCode, expected);
    }

    [Fact]
    public async Task GivenJsonSerializerDeserializeWithDerivedType_ThenReportsDiagnostic()
    {
        var testCode = """
            using System.Text.Json;
            using System.Text.Json.Nodes;
            using Ignixa.Serialization.SourceNodes;

            namespace Ignixa.Serialization.SourceNodes
            {
                public class PatientJsonNode : ResourceJsonNode
                {
                    public PatientJsonNode(JsonObject obj) : base(obj) { }
                }
            }

            class TestClass
            {
                void TestMethod()
                {
                    var json = "{}";
                    var resource = JsonSerializer.Deserialize<PatientJsonNode>(json);
                }
            }
            """;

        var expected = new DiagnosticResult(ResourceJsonNodeDeserializeAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
            .WithSpan(18, 24, 18, 77)
            .WithArguments("PatientJsonNode");

        await VerifyAnalyzerAsync(testCode, expected);
    }

    [Fact]
    public async Task GivenJsonSerializerDeserializeWithOtherType_ThenNoDiagnostic()
    {
        var testCode = """
            using System.Text.Json;

            class MyClass { }

            class TestClass
            {
                void TestMethod()
                {
                    var json = "{}";
                    var obj = JsonSerializer.Deserialize<MyClass>(json);
                }
            }
            """;

        await VerifyAnalyzerAsync(testCode);
    }

    [Fact]
    public async Task GivenResourceJsonNodeParse_ThenNoDiagnostic()
    {
        var testCode = """
            using Ignixa.Serialization.SourceNodes;

            class TestClass
            {
                void TestMethod()
                {
                    var json = "{}";
                    var resource = ResourceJsonNode.Parse(json);
                }
            }
            """;

        await VerifyAnalyzerAsync(testCode);
    }

    private static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<ResourceJsonNodeDeserializeAnalyzer, XUnitVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        // Add reference to Ignixa.Serialization
        test.TestState.AdditionalReferences.Add(typeof(Ignixa.Serialization.SourceNodes.ResourceJsonNode).Assembly);

        test.ExpectedDiagnostics.AddRange(expected);

        await test.RunAsync();
    }
}

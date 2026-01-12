using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;

namespace Ignixa.Analyzers.Tests;

public class ResourceJsonNodeDeserializeCodeFixTests
{
    [Fact]
    public async Task GivenJsonSerializerDeserialize_WhenApplyingCodeFix_ThenReplacesWithParse()
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

        var fixedCode = """
            using System.Text.Json;
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

        var expected = new DiagnosticResult(ResourceJsonNodeDeserializeAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
            .WithSpan(9, 24, 9, 79)
            .WithArguments("ResourceJsonNode");

        await VerifyCodeFixAsync(testCode, expected, fixedCode);
    }

    [Fact]
    public async Task GivenJsonSerializerDeserializeWithDerivedType_WhenApplyingCodeFix_ThenReplacesWithParse()
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
                    public static PatientJsonNode Parse(string json) => throw new System.NotImplementedException();
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

        var fixedCode = """
            using System.Text.Json;
            using System.Text.Json.Nodes;
            using Ignixa.Serialization.SourceNodes;

            namespace Ignixa.Serialization.SourceNodes
            {
                public class PatientJsonNode : ResourceJsonNode
                {
                    public PatientJsonNode(JsonObject obj) : base(obj) { }
                    public static PatientJsonNode Parse(string json) => throw new System.NotImplementedException();
                }
            }

            class TestClass
            {
                void TestMethod()
                {
                    var json = "{}";
                    var resource = PatientJsonNode.Parse(json);
                }
            }
            """;

        var expected = new DiagnosticResult(ResourceJsonNodeDeserializeAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
            .WithSpan(19, 24, 19, 77)
            .WithArguments("PatientJsonNode");

        await VerifyCodeFixAsync(testCode, expected, fixedCode);
    }

    private static async Task VerifyCodeFixAsync(string source, DiagnosticResult expected, string fixedSource)
    {
        var test = new CSharpCodeFixTest<
            ResourceJsonNodeDeserializeAnalyzer,
            ResourceJsonNodeDeserializeCodeFixProvider,
            XUnitVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        // Add reference to Ignixa.Serialization
        test.TestState.AdditionalReferences.Add(typeof(Ignixa.Serialization.SourceNodes.ResourceJsonNode).Assembly);
        test.FixedState.AdditionalReferences.Add(typeof(Ignixa.Serialization.SourceNodes.ResourceJsonNode).Assembly);

        test.ExpectedDiagnostics.Add(expected);

        await test.RunAsync();
    }
}

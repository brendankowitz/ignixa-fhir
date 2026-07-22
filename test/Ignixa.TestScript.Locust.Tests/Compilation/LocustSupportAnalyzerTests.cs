using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Locust.Compilation;
using Ignixa.TestScript.Locust.Diagnostics;
using Ignixa.TestScript.Model;

namespace Ignixa.TestScript.Locust.Tests.Compilation;

public class LocustSupportAnalyzerTests
{
    private static TestScriptDefinition Build(ActionExpression action) => new()
    {
        Metadata = new TestScriptMetadata { Name = "Suite" },
        Tests = [new TestPhaseDefinition { Name = "case", Actions = [action] }]
    };

    [Fact]
    public void GivenSupportedSingleDestinationDefinition_WhenAnalyzed_ThenNoErrorDiagnostics()
    {
        TestScriptDefinition definition = Build(new OperationExpression
        {
            Type = "read",
            Destination = 1
        });

        IReadOnlyList<LocustDiagnostic> diagnostics = LocustSupportAnalyzer.Analyze(definition, "supported.json");

        diagnostics.ShouldNotContain(d => d.Severity == LocustDiagnosticSeverity.Error);
    }

    [Fact]
    public void GivenMultiDestinationOriginAndTargetIdOperation_WhenAnalyzed_ThenEmitsThreeErrorsFromCanonicalSource()
    {
        TestScriptDefinition definition = Build(new OperationExpression
        {
            Type = "read",
            Destination = 2,
            Origin = 1,
            TargetId = "target"
        });

        IReadOnlyList<LocustDiagnostic> diagnostics = LocustSupportAnalyzer.Analyze(definition, "unsupported.json");

        List<LocustDiagnostic> errors = [.. diagnostics.Where(d => d.Severity == LocustDiagnosticSeverity.Error)];

        errors.Count.ShouldBe(3);
        errors.Select(e => e.Code).ShouldBe(["LOCUST001", "LOCUST002", "LOCUST003"], ignoreOrder: true);
        errors.ShouldAllBe(e => e.Source.StartsWith("unsupported.json:test:case:action:0", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenUnencodedUrlAndProfileReference_WhenAnalyzed_ThenEmitsEncodingAndProfileWarnings()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Profiles =
            [
                new ProfileReference
                {
                    Id = "profile",
                    Canonical = "http://example.test/StructureDefinition/patient"
                }
            ],
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "case",
                    Actions =
                    [
                        new OperationExpression
                        {
                            Type = "read",
                            Destination = 1,
                            EncodeRequestUrl = false
                        }
                    ]
                }
            ]
        };

        IReadOnlyList<LocustDiagnostic> diagnostics = LocustSupportAnalyzer.Analyze(definition, "warnings.json");

        diagnostics.Select(d => d.Code).ShouldContain("LOCUST004");
        diagnostics.Select(d => d.Code).ShouldContain("LOCUST005");
        diagnostics.ShouldAllBe(d => d.Severity != LocustDiagnosticSeverity.Error);
    }

    [Fact]
    public void GivenSetupTestsAndTeardown_WhenAnalyzed_ThenSourcesAndOrderAreCanonicalAndStable()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Setup =
            [
                new OperationExpression { Type = "delete", Destination = 2 }
            ],
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "case-one",
                    Actions =
                    [
                        new OperationExpression { Type = "create", Destination = 2 },
                        new OperationExpression { Type = "update", Destination = 2 }
                    ]
                },
                new TestPhaseDefinition
                {
                    Name = "case-two",
                    Actions =
                    [
                        new OperationExpression { Type = "read", Destination = 2 }
                    ]
                }
            ],
            Teardown =
            [
                new OperationExpression { Type = "delete", Destination = 2 }
            ]
        };

        IReadOnlyList<LocustDiagnostic> diagnostics = LocustSupportAnalyzer.Analyze(definition, "traversal.json");

        diagnostics.Select(d => d.Source).ShouldBe(
        [
            "traversal.json:setup:action:0",
            "traversal.json:test:case-one:action:0",
            "traversal.json:test:case-one:action:1",
            "traversal.json:test:case-two:action:0",
            "traversal.json:teardown:action:0"
        ]);
        diagnostics.ShouldAllBe(d => d.Code == "LOCUST001");
    }

    [Fact]
    public void GivenUnknownActionExpressionSubtype_WhenAnalyzed_ThenEmitsLocust006()
    {
        TestScriptDefinition definition = Build(new UnknownActionExpression());

        IReadOnlyList<LocustDiagnostic> diagnostics = LocustSupportAnalyzer.Analyze(definition, "unknown-action.json");

        diagnostics.ShouldContain(d => d.Code == "LOCUST006" && d.Severity == LocustDiagnosticSeverity.Error);
    }

    [Fact]
    public void GivenUnknownAssertCriteriaSubtype_WhenAnalyzed_ThenEmitsLocust006()
    {
        TestScriptDefinition definition = Build(new AssertExpression { Criteria = new UnknownAssertCriteria() });

        IReadOnlyList<LocustDiagnostic> diagnostics = LocustSupportAnalyzer.Analyze(definition, "unknown-criteria.json");

        diagnostics.ShouldContain(d => d.Code == "LOCUST006" && d.Severity == LocustDiagnosticSeverity.Error);
    }

    [Fact]
    public void GivenNullDefinition_WhenAnalyzed_ThenThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => LocustSupportAnalyzer.Analyze(null!, "source.json"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GivenBlankSource_WhenAnalyzed_ThenThrowsArgumentException(string? source)
    {
        TestScriptDefinition definition = Build(new OperationExpression { Type = "read", Destination = 1 });

        Should.Throw<ArgumentException>(() => LocustSupportAnalyzer.Analyze(definition, source!));
    }

    private sealed record UnknownActionExpression : ActionExpression
    {
        public override ValueTask<TestScriptContext> AcceptAsync(
            ITestScriptActionVisitor visitor,
            TestScriptContext context,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("Test-only action type used to exercise LOCUST006.");
    }

    private sealed record UnknownAssertCriteria : AssertCriteria;
}

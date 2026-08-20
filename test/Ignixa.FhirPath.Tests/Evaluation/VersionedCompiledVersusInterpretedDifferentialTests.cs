/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Differential harness for the version-gated half of the type operators.
 *
 * CompiledVersusInterpretedDifferentialTests holds the two paths to the same answer at one FHIR
 * version. That is enough for the rules that do not read the version and blind to the ones that do:
 * `as` accepts capitalised aliases below R5 and rejects them from R5 on, and the branch exempting
 * engine-produced System values from that gate is only reachable when the context carries a schema.
 * A compiled-versus-interpreted divergence that existed only on R5 and R6 passed every differential
 * suite because none of them supplied one.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Specification;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class VersionedCompiledVersusInterpretedDifferentialTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();
    private readonly FhirPathDelegateCompiler _compiler = new(new FhirPathEvaluator());

    public static TheoryData<FhirVersion, string> Corpus
    {
        get
        {
            var data = new TheoryData<FhirVersion, string>();
            foreach (var version in DifferentialFixture.PublishedVersions)
            {
                foreach (var row in DifferentialFixture.TypeOperatorCorpus)
                {
                    data.Add(version, row);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void GivenATypeOperatorOnAnyPublishedVersion_WhenEvaluatedByBothPaths_ThenResultsAreIdentical(
        FhirVersion version,
        string expression)
    {
        // Arrange
        var subject = DifferentialFixture.CreateSubject(version);
        var ast = _parser.Parse(expression);
        var compiled = _compiler.TryCompile(ast);

        if (compiled is null)
        {
            // Declining to compile is the designed escape hatch: Select() falls back to the
            // interpreter, so the two paths agree by construction and there is nothing to compare.
            return;
        }

        // Act
        var compiledResult = DifferentialFixture.Describe(
            () => compiled(subject, DifferentialFixture.CreateContext(subject, version)));
        var interpretedResult = DifferentialFixture.Describe(
            () => _evaluator.Evaluate(subject, ast, DifferentialFixture.CreateContext(subject, version)));

        // Assert
        compiledResult.ShouldBe(
            interpretedResult,
            $"Compiled and interpreted evaluation of '{expression}' disagree on {version}.");
    }
}

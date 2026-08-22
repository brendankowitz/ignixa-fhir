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

    /// <summary>
    /// The rows of <see cref="DifferentialFixture.TypeOperatorCorpus"/> that actually reach the
    /// compiled path, named so that a row leaving that set fails instead of silently joining the
    /// declined majority.
    /// </summary>
    /// <remarks>
    /// Measured, these eight are the whole of it: 21 of the 29 rows are declined and reach the
    /// early-out below, where the two paths agree because only one of them ran. Without this list the
    /// last row could stop compiling and the sweep would still be green over 145 cases, every one of
    /// them a no-op. Mirrors <c>CompiledVersusInterpretedDifferentialTests.MustCompile</c>.
    /// </remarks>
    public static TheoryData<string> MustCompile => new()
    {
        "birthDate.count().ofType(Integer)",
        "birthDate.count().ofType(integer)",
        "birthDate.exists().ofType(Boolean)",
        "birthDate.exists().ofType(boolean)",
        "name.count() > 0",
        "birthDate.ofType(date)",
        "birthDate.ofType(Date)",
        "name.ofType(HumanName).family",
    };

    /// <summary>
    /// Asserts <see cref="MustCompile"/> is a subset of <see cref="DifferentialFixture.TypeOperatorCorpus"/>.
    /// </summary>
    /// <remarks>
    /// The two lists are maintained by hand in different files. Without this, a row could be edited out
    /// of <c>TypeOperatorCorpus</c> while surviving in <c>MustCompile</c> - or renamed in one file and not
    /// the other - and both suites would stay green while comparing nothing for that row.
    /// </remarks>
    [Fact]
    public void GivenMustCompile_WhenComparedAgainstTheCorpus_ThenEveryRowIsAMemberOfIt()
    {
        // Arrange
        var corpus = new HashSet<string>(StringComparer.Ordinal);
        foreach (string expression in DifferentialFixture.TypeOperatorCorpus)
        {
            corpus.Add(expression);
        }

        var mustCompile = new List<string>();
        foreach (string expression in MustCompile)
        {
            mustCompile.Add(expression);
        }

        // Act & Assert
        mustCompile.ShouldAllBe(
            expression => corpus.Contains(expression),
            "every row in MustCompile has to also be a row in TypeOperatorCorpus, or this suite is " +
            "asserting compilation for an expression the differential theory never runs.");
    }

    /// <summary>
    /// Asserts the <see cref="MustCompile"/> inventory is complete, so a row cannot be deleted from it
    /// without failing the build.
    /// </summary>
    /// <remarks>
    /// The subset check above cannot catch deletion: removing a row still leaves every remaining row a
    /// member of <c>TypeOperatorCorpus</c>, so that check stays green with fewer rows compared. The
    /// expected list here is written independently of <see cref="MustCompile"/> for the same reason
    /// <c>FirelyVersusIgnixaDifferentialTests.NormalisedTypeNames</c>'s inventory test is: a list derived
    /// from the collection it guards agrees with any edit to that collection and asserts nothing.
    /// </remarks>
    [Fact]
    public void GivenTheMustCompileInventory_WhenEnumerated_ThenEveryPinnedExpressionIsPresent()
    {
        // Arrange
        string[] expected =
        [
            "birthDate.count().ofType(Integer)",
            "birthDate.count().ofType(integer)",
            "birthDate.exists().ofType(Boolean)",
            "birthDate.exists().ofType(boolean)",
            "name.count() > 0",
            "birthDate.ofType(date)",
            "birthDate.ofType(Date)",
            "name.ofType(HumanName).family",
        ];

        var actual = new List<string>();
        foreach (string expression in MustCompile)
        {
            actual.Add(expression);
        }

        // Assert
        actual.ToArray().ShouldBe(
            expected,
            "The MustCompile inventory changed. A row may only be retired after confirming it no longer "
            + "carries differential coverage; update this inventory in the same change.");
    }

    /// <summary>
    /// One literal expected answer per (row, version), captured against the interpreter once and
    /// written by hand - independent of both evaluation paths, so agreement between them is no longer
    /// the thing being asserted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The eight <see cref="MustCompile"/> rows are the ones where the theory below actually exercises
    /// both evaluation paths, so they are the ones where "both paths agree" could mean "both paths
    /// share the same bug" - exactly the shape of the defect this class exists to catch (see the class
    /// remarks). <c>birthDate.ofType(Date)</c> is the one <c>MustCompile</c> row whose answer is
    /// version-gated: R5 2.1.9.1.2 withdraws the pre-R5 System-spelling alias for the cast operators, so
    /// the capitalised <c>Date</c> spelling stops matching the lower-case <c>date</c> instance from R5
    /// on. Every other <c>MustCompile</c> row is version-independent because the rule that saves it -
    /// either an exact instance-type match, or the System-spelling alias for an engine-produced value,
    /// which <c>TypeMatcher.TypeNamesMatch</c> checks above the R5 gate - does not read the version at
    /// all.
    /// </para>
    /// <para>
    /// The four <c>name is</c>/<c>name.as()</c> rows never compile (<c>is</c>/<c>as</c> are not in
    /// <see cref="FhirPathDelegateCompiler.CompileBinary"/>'s operator set), so pinning them here checks
    /// only the interpreter - but it replaces a blanket "did not throw" with the exact answer, including
    /// exactly when it throws. <c>name</c> on the fixture subject has two items. <c>is</c>'s singleton
    /// rule is unconditional on every version (<c>TypeMatcher.EnsureSingletonTypeTestInput</c>), so both
    /// <c>name is HumanName</c> and <c>name is humanname</c> throw on all five - the corpus's deliberate
    /// probe for that rule, and the reason <c>name is HumanName</c> is listed in
    /// <see cref="DifferentialFixture"/>'s <c>ErrorProbes</c>. <c>as</c>'s singleton rule is enforced only
    /// from R5 (<c>TypeMatcher.EnsureSingletonInput</c>), so <c>name.as(HumanName).family</c> succeeds on
    /// Stu3/R4/R4B and throws only on R5/R6 - a genuine version-gated divergence that a flat, version-blind
    /// error-probe list cannot express, which is why it is pinned here instead of added there.
    /// <c>name.as(humanname).family</c> never matches at all (Ordinal comparison rejects the mis-cased
    /// spelling - see the class remarks on <c>TypeMatcher</c>), so pre-R5 it is empty rather than
    /// Smith/Jones, and R5 on it throws for the same cardinality reason as its correctly-cased sibling.
    /// </para>
    /// </remarks>
    private static string[]? ExpectedResult(string expression, FhirVersion version) => expression switch
    {
        "birthDate.count().ofType(Integer)" => ["integer|Int32|1"],
        "birthDate.count().ofType(integer)" => ["integer|Int32|1"],
        "birthDate.exists().ofType(Boolean)" => ["boolean|Boolean|true"],
        "birthDate.exists().ofType(boolean)" => ["boolean|Boolean|true"],
        "name.count() > 0" => ["boolean|Boolean|true"],
        "birthDate.ofType(date)" => ["date|FhirTemporal|1974-12-25"],
        "birthDate.ofType(Date)" => version is FhirVersion.Stu3 or FhirVersion.R4 or FhirVersion.R4B
            ? ["date|FhirTemporal|1974-12-25"]
            : [],
        "name.ofType(HumanName).family" => ["string|String|Smith", "string|String|Jones"],

        "name is HumanName" => ["threw:FhirPathEvaluationException"],
        "name is humanname" => ["threw:FhirPathEvaluationException"],

        // "HumanName" matches the instance type exactly, so pre-R5 (before the singleton-cast rule is
        // enforced) it selects both names; R5 on, the cardinality check runs first and throws.
        "name.as(HumanName).family" => version is FhirVersion.Stu3 or FhirVersion.R4 or FhirVersion.R4B
            ? ["string|String|Smith", "string|String|Jones"]
            : ["threw:FhirPathEvaluationException"],

        // "humanname" never matches - TypeMatcher's Ordinal comparison rejects the mis-cased spelling on
        // every version (TypeMatcher remarks: "as(humanname) selects nothing"). Pre-R5 that empty answer
        // is reached; R5 on, the cardinality check still runs first and throws before matching happens.
        "name.as(humanname).family" => version is FhirVersion.Stu3 or FhirVersion.R4 or FhirVersion.R4B
            ? []
            : ["threw:FhirPathEvaluationException"],

        _ => null,
    };

    private static bool PredictsThrow(string[]? expected) =>
        expected is [var single] && single.StartsWith("threw:", StringComparison.Ordinal);

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
        var expected = ExpectedResult(expression, version);

        // Act
        var interpretedResult = DifferentialFixture.Describe(
            () => _evaluator.Evaluate(subject, ast, DifferentialFixture.CreateContext(subject, version)));

        // Assert: a thrown exception is never "agreement", whichever path produced it, and is checked
        // whether or not this row reaches the compiled path - unless ExpectedResult already predicts and
        // pins the throw for this exact (row, version), which is strictly more specific than the
        // "did not throw" floor AssertEvaluated provides.
        if (!PredictsThrow(expected))
        {
            DifferentialFixture.AssertEvaluated(interpretedResult, expression);
        }

        if (expected is not null)
        {
            interpretedResult.ShouldBe(
                expected,
                $"Interpreted evaluation of '{expression}' on {version} should be [{string.Join(", ", expected)}].");
        }

        if (compiled is null)
        {
            // Declining to compile is the designed escape hatch: Select() falls back to the
            // interpreter, so there is no second path to compare here. MustCompile guards against this
            // becoming true for a row it should not be true for.
            return;
        }

        var compiledResult = DifferentialFixture.Describe(
            () => compiled(subject, DifferentialFixture.CreateContext(subject, version)));
        DifferentialFixture.AssertEvaluated(compiledResult, expression);

        if (expected is not null)
        {
            // The real check for a MustCompile row: against the independently written answer, not
            // merely against whatever the interpreter also produced.
            compiledResult.ShouldBe(
                expected,
                $"Compiled evaluation of '{expression}' on {version} should be [{string.Join(", ", expected)}].");
        }
        else
        {
            // A row outside MustCompile that starts compiling has no literal oracle yet; agreement with
            // the interpreter is a floor, not the final word - see MustCompile's remarks.
            compiledResult.ShouldBe(
                interpretedResult,
                $"Compiled and interpreted evaluation of '{expression}' disagree on {version}.");
        }
    }

    [Theory]
    [MemberData(nameof(MustCompile))]
    public void GivenATypeOperatorRowThatCarriesDifferentialCoverage_WhenCompiled_ThenCompilationIsNotDeclined(
        string expression)
    {
        // Arrange
        var ast = _parser.Parse(expression);

        // Act
        var compiled = _compiler.TryCompile(ast);

        // Assert
        compiled.ShouldNotBeNull(
            $"'{expression}' is one of the eight rows this sweep actually compares. If it stops compiling "
            + "the theory above turns into a no-op for it on all five versions and still reports green.");
    }
}

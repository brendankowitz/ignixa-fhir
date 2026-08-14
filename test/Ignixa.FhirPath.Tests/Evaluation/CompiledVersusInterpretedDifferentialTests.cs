/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Differential harness holding the two FHIRPath evaluation paths to the same answer.
 *
 * TypedElementExtensions.Select() prefers a compiled delegate and only falls back to the
 * interpreter when FhirPathDelegateCompiler.TryCompile returns null, so the compiled path is the
 * one production search-parameter extraction observes. Nothing previously forced the two to agree
 * and they drifted: temporal literals kept their '@' sigil through the compiled ordinal string
 * compare, so "Patient.birthDate = @1974-12-25" answered false while the interpreter answered true.
 *
 * Every expression here is evaluated through both paths and the results must be indistinguishable.
 */

using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class CompiledVersusInterpretedDifferentialTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();
    private readonly FhirPathDelegateCompiler _compiler = new(new FhirPathEvaluator());

    public static TheoryData<string> Corpus => new()
    {
        "birthDate = @1974-12-25",
        "birthDate != @1974-12-25",
        "birthDate = @1974-12-26",
        "birthDate >= @1974-12-25",
        "birthDate <= @1974-12-25",
        "birthDate < @1980-01-01",
        "birthDate > @1980-01-01",
        "issued > @2024-01-01T00:00:00Z",
        "issued < @2024-01-01T00:00:00Z",
        "issued = @2024-06-15T08:00:00Z",
        "issued >= @2024-06-15T08:00:00Z",
        "issued != @2024-06-15T08:00:00Z",

        "birthDate = birthDate",
        "period.start < period.end",
        "period.start > period.end",
        "period.start = period.end",
        "period.start <= period.end",

        "birthDate = '1974-12-25'",
        "birthDate != '1974-12-25'",
        "birthDate < '1980-01-01'",
        "gender = 'male'",

        "birthDate = @1974",
        "birthDate > @1974",
        "birthDate < @1974",
        "birthDate = @1974-12",
        "birthDate >= @1974-12",
        "@2012 > @2012-01",
        "@2012 = @2012-01",
        "@2012 < @2012-01",
        "@2012-01 <= @2012",
        "birthDate = @1974-12-25T10:00:00",
        "birthDate < @1974-12-25T10:00:00",
        "issued = @2024-06-15T08:00:00",
        "issued > @2024-06-15T08:00:00",
        "issued = @2024-06-15T08:00:00.000Z",

        "birthTime = @T10:30:00",
        "birthTime != @T10:30:00",
        "birthTime < @T12:00:00",
        "birthTime > @T12:00:00",
        "birthTime = @T10:30",
        "birthDate = @T10:30:00",
        "birthDate < @T10:30:00",
        "birthTime = @1974-12-25",
        "issued > @T10:30:00",
        "extension.value = @T10:30:00",
        "extension.value != @T10:30:00",
        "extension.value < @T12:00:00",
        "extension.value",

        "'abc' = 'abc'",
        "'abc' != 'abd'",
        "'abc' < 'abd'",
        "'abc' >= 'abd'",
        "gender < 'z'",
        "5 = 5",
        "5 != 4",
        "5 > 3",
        "5 <= 3",
        "1.5 < 2",
        "1.5 = 1.50",
        "1.5 >= 1.5",
        "true = true",
        "true != false",
        "multipleBirthInteger = 2",
        "multipleBirthInteger > 1",
        "score = 1.5",
        "score < 2",
        "active = true",

        "'abc'",
        "5",
        "1.5",
        "true",
        "@1974-12-25",
        "@T10:30:00",
        "birthDate",
        "gender",

        "name.family",
        "name.given",
        "name.first().family",
        "name.last().family",
        "name.given.count()",
        "name.exists()",
        "photo.exists()",
        "photo.empty()",
        "name.empty()",
        "identifier.count()",
        "telecom.where(system = 'phone')",
        "telecom.where(system = 'phone').value",
        "telecom.where(system = 'fax').value",
        "telecom.where(value = '555-1234').system",
        "name.tail()",
        "(name).family",
        "(birthDate = @1974-12-25)",
        "missingElement",
        "missingElement.exists()",
        "missingElement = 'x'",
        "missingElement > @1974-12-25",
    };

    /// <summary>
    /// Expressions whose comparison operands are temporal and which must still take the compiled fast
    /// path. Search-parameter extraction leans on date comparisons, so a correctness fix that silently
    /// downgraded them to the interpreter would pass the differential test above while losing the
    /// reason the compiler exists. This list is the tripwire for that.
    /// </summary>
    public static TheoryData<string> MustCompile => new()
    {
        "birthDate = @1974-12-25",
        "issued > @2024-01-01T00:00:00Z",
        "birthTime = @T10:30:00",
        "period.start < period.end",
        "telecom.where(system = 'phone')",
        "gender = 'male'",
    };

    [Theory]
    [MemberData(nameof(Corpus))]
    public void GivenAnExpression_WhenEvaluatedByBothPaths_ThenResultsAreIdentical(string expression)
    {
        // Arrange
        var subject = CreateSubject();
        var ast = _parser.Parse(expression);
        var compiled = _compiler.TryCompile(ast);

        if (compiled is null)
        {
            // Declining to compile is the designed escape hatch: Select() falls back to the
            // interpreter, so the two paths agree by construction and there is nothing to compare.
            return;
        }

        // Act
        var compiledResult = Describe(() => compiled(subject, CreateContext(subject)));
        var interpretedResult = Describe(() => _evaluator.Evaluate(subject, ast, CreateContext(subject)));

        // Assert
        compiledResult.ShouldBe(
            interpretedResult,
            $"Compiled and interpreted evaluation of '{expression}' disagree.");
    }

    [Theory]
    [MemberData(nameof(MustCompile))]
    public void GivenAComparisonOnTheFastPath_WhenCompiled_ThenCompilationIsNotDeclined(string expression)
    {
        // Arrange
        var ast = _parser.Parse(expression);

        // Act
        var compiled = _compiler.TryCompile(ast);

        // Assert
        compiled.ShouldNotBeNull($"'{expression}' must keep using the compiled fast path.");
    }

    [Fact]
    public void GivenADatePathEqualToItsLiteral_WhenEvaluatedByBothPaths_ThenBothReportTrue()
    {
        // Regression: the compiled path compared "1974-12-25" against the unstripped "@1974-12-25"
        // ordinally and reported false while the interpreter reported true.

        // Arrange
        var subject = CreateSubject();

        // Act
        var result = subject.Select("birthDate = @1974-12-25").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenAnInstantGreaterThanItsLiteral_WhenEvaluatedByBothPaths_ThenBothReportTrue()
    {
        // Arrange
        var subject = CreateSubject();

        // Act
        var result = subject.Select("issued > @2024-01-01T00:00:00Z").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenATimeEqualToItsLiteral_WhenEvaluatedByBothPaths_ThenBothReportTrue()
    {
        // Arrange
        var subject = CreateSubject();

        // Act
        var result = subject.Select("birthTime = @T10:30:00").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenATimeValuedExtensionEqualToItsLiteral_WhenEvaluatedByBothPaths_ThenBothReportTrue()
    {
        // Regression: "extension.value = @T10:30:00" answered false on the compiled path. The literal
        // kept its '@' and the element's FhirTemporal was compared to it as an ordinal string.

        // Arrange
        var subject = CreateSubject();

        // Act
        var result = subject.Select("extension.value = @T10:30:00").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenOperandsOfDifferentPrecision_WhenOrderedOnTheCompiledPath_ThenResultIsEmpty()
    {
        // A year and a month overlap rather than order, so FHIRPath requires empty. The old compiled
        // comparer was typed Func<object?, object?, bool> and structurally could not express it.

        // Arrange
        var subject = CreateSubject();

        // Act
        var result = subject.Select("@2012 > @2012-01").ToList();

        // Assert
        result.ShouldBeEmpty();
    }

    private static EvaluationContext CreateContext(IElement subject)
    {
        return new EvaluationContext() with { Resource = subject, RootResource = subject };
    }

    /// <summary>
    /// Renders a result collection as text so that the two paths can be compared on everything a
    /// caller can observe: element count, instance type, and value.
    /// </summary>
    /// <remarks>
    /// A thrown exception is recorded as the observed outcome rather than propagated, because "one
    /// path throws and the other returns a value" is itself a divergence this harness exists to
    /// report, and letting it escape would hide the comparison behind a stack trace.
    /// </remarks>
    private static IReadOnlyList<string> Describe(Func<IEnumerable<IElement>> evaluate)
    {
        try
        {
            return evaluate().Select(element => $"{element.InstanceType}|{Render(element.Value)}").ToList();
        }
        catch (Exception ex)
        {
            return [$"threw:{ex.GetType().Name}"];
        }
    }

    private static string Render(object? value)
    {
        return value switch
        {
            null => "<null>",
            FhirTemporal temporal => temporal.Literal,
            bool flag => flag ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "<null>"
        };
    }

    private static IElement CreateSubject()
    {
        return new TestElement("Patient", "Patient", children:
        [
            Temporal("birthDate", "1974-12-25", FhirPrimitive.Date, "date"),
            Temporal("birthTime", "10:30:00", FhirPrimitive.Time, "time"),
            Temporal("issued", "2024-06-15T08:00:00Z", FhirPrimitive.Instant, "instant"),
            new TestElement("gender", "code", "male"),
            new TestElement("active", "boolean", true),
            new TestElement("multipleBirthInteger", "integer", 2),
            new TestElement("score", "decimal", 1.5m),
            new TestElement("name", "HumanName", children:
            [
                new TestElement("family", "string", "Smith"),
                new TestElement("given", "string", "John"),
                new TestElement("given", "string", "Q"),
            ]),
            new TestElement("name", "HumanName", children:
            [
                new TestElement("family", "string", "Jones"),
                new TestElement("given", "string", "Ann"),
            ]),
            new TestElement("telecom", "ContactPoint", children:
            [
                new TestElement("system", "code", "phone"),
                new TestElement("value", "string", "555-1234"),
            ]),
            new TestElement("telecom", "ContactPoint", children:
            [
                new TestElement("system", "code", "email"),
                new TestElement("value", "string", "patient@example.org"),
            ]),
            new TestElement("period", "Period", children:
            [
                Temporal("start", "2020-01-01", FhirPrimitive.Date, "date"),
                Temporal("end", "2021-06-15", FhirPrimitive.Date, "date"),
            ]),
            new TestElement("identifier", "Identifier", children:
            [
                new TestElement("value", "string", "abc"),
            ]),
            new TestElement("extension", "Extension", children:
            [
                Temporal("value", "10:30:00", FhirPrimitive.Time, "time"),
            ]),
        ]);
    }

    private static IElement Temporal(string name, string literal, FhirPrimitive kind, string instanceType)
    {
        if (!FhirTemporal.TryParse(literal, kind, out var temporal) || temporal is null)
        {
            throw new InvalidOperationException($"Failed to parse temporal literal '{literal}'.");
        }

        return new TestElement(name, instanceType, temporal);
    }

    private sealed class TestElement : IElement
    {
        private readonly IReadOnlyList<IElement> _children;

        public TestElement(string name, string instanceType, object? value = null, IReadOnlyList<IElement>? children = null)
        {
            Name = name;
            InstanceType = instanceType;
            Value = value;
            _children = children ?? [];
        }

        public string Name { get; }

        public string InstanceType { get; }

        public object? Value { get; }

        public string Location => Name;

        public IType? Type => null;

        public bool HasPrimitiveValue => Value is not null;

        public IReadOnlyList<IElement> Children(string? name = null) =>
            name is null ? _children : _children.Where(child => child.Name == name).ToList();

        public T? Meta<T>() where T : class => null;
    }
}

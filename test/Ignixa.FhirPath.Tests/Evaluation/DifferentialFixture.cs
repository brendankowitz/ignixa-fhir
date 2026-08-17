/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Shared corpus, subject and result rendering for the FHIRPath differential harnesses.
 */

using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// The expressions, subject resource and result rendering shared by every differential harness.
/// </summary>
/// <remarks>
/// There are three ways an expression can reach an answer - the interpreter, the compiled delegate,
/// and the optimizing parser's constant-folded AST - and a harness that compares only two of them is
/// how a divergence survives review. Sharing the corpus and the subject here keeps each pair of paths
/// answering the same questions about the same data.
/// </remarks>
internal static class DifferentialFixture
{
    /// <summary>
    /// Expressions every pair of evaluation paths must agree on.
    /// </summary>
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
    /// Expressions in which one operand is a compile-time constant and the other either signals an
    /// error or is not boolean.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the shapes the optimizing parser rewrites at parse time, and the ones that can only
    /// be checked by comparing an optimized parse against an unoptimized one - the compiled/interpreted
    /// harness never sees them because both of its paths consume the same unoptimized AST.
    /// </para>
    /// <para>
    /// <c>(1 | 2).single()</c> is the error probe: it throws <see cref="FhirPathEvaluationException"/>,
    /// so any rewrite that drops the operand containing it turns a thrown error into a quiet answer.
    /// </para>
    /// </remarks>
    public static TheoryData<string> FoldableCorpus => new()
    {
        "(1 | 2).single().exists() and false",
        "false and (1 | 2).single().exists()",
        "(1 | 2).single().exists() and true",
        "true and (1 | 2).single().exists()",
        "(1 | 2).single().exists() or true",
        "true or (1 | 2).single().exists()",
        "(1 | 2).single().exists() or false",
        "false or (1 | 2).single().exists()",
        "(1 | 2).single().exists() implies true",
        "false implies (1 | 2).single().exists()",
        "true implies (1 | 2).single().exists()",

        "name.family.single().exists() and false",
        "name.family.single().exists() or true",
        "name.family.single().exists() implies true",

        "active and false",
        "false and active",
        "active and true",
        "true and active",
        "active or true",
        "true or active",
        "active or false",
        "false or active",
        "active implies true",
        "false implies active",
        "true implies active",

        "photo.exists() and false",
        "photo.exists() or true",
        "missingElement and false",
        "missingElement or true",
        "missingElement and true",
        "missingElement or false",
        "missingElement implies true",

        "birthDate and false",
        "birthDate or true",
        "name and false",
        "name.given and true",

        "(1 | 2).single() * 0",
        "0 * (1 | 2).single()",
        "(1 | 2).single() * 1",
        "(1 | 2).single() + 0",
        "(1 | 2).single() - 0",
        "0 / (1 | 2).single()",
        "(1 | 2).single() / 1",
        "(1 | 2).single() & ''",
        "'' & (1 | 2).single()",

        "multipleBirthInteger * 0",
        "0 * multipleBirthInteger",
        "score * 0",
        "0 / multipleBirthInteger",
        "0 / score",
        "missingElement * 0",
        "gender & ''",
        "'' & gender",
        "multipleBirthInteger / 1",
        "score / 1",
        "multipleBirthInteger * 1",
        "multipleBirthInteger + 0",
        "0 + multipleBirthInteger",
        "multipleBirthInteger - 0",
        "gender + 0",
        "gender * 1",
        "birthDate + 0",
        "missingElement + 0",
        "missingElement & ''",

        "(1 | 2).single().where(false)",
        "(1 | 2).single().where(true)",
        "name.family.where(false)",
        "name.family.where(true)",
        "missingElement.where(false)",

        "iif(false, (1 | 2).single(), 'fallback')",
        "iif(true, 'taken', (1 | 2).single())",

        "name.not().not()",
        "active.not().not()",
        "(1 | 2).single().not().not()",
        "name.first().first()",
        "(1 | 2).single().exists()",
        "(1 | 2).single().empty()",
        "(1 | 2).single().count()",
        "(1 | 2).single().toString()",

        "@2012 and false",
        "@2012 = @2012",
        "@T10:30 < @T11:00",
        "@2012-01-01 + 0",

        "(1 | 2).single().exists() and false and true",
        "true or ((1 | 2).single().exists() and true)",
    };

    public static EvaluationContext CreateContext(IElement subject)
    {
        return new EvaluationContext() with { Resource = subject, RootResource = subject };
    }

    /// <summary>
    /// Renders a result collection as text so that two paths can be compared on everything a caller
    /// can observe: element count, instance type, and value.
    /// </summary>
    /// <remarks>
    /// A thrown exception is recorded as the observed outcome rather than propagated, because "one
    /// path throws and the other returns a value" is itself a divergence this harness exists to
    /// report, and letting it escape would hide the comparison behind a stack trace.
    /// </remarks>
    public static IReadOnlyList<string> Describe(Func<IEnumerable<IElement>> evaluate)
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

    public static string Render(object? value)
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

    public static IElement CreateSubject()
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

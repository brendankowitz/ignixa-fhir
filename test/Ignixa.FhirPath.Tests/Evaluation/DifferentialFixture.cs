/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Shared corpus, subject and result rendering for the FHIRPath differential harnesses.
 */

using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

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
        "meta.lastUpdated > @2024-01-01T00:00:00Z",
        "meta.lastUpdated < @2024-01-01T00:00:00Z",
        "meta.lastUpdated = @2024-06-15T08:00:00Z",
        "meta.lastUpdated >= @2024-06-15T08:00:00Z",
        "meta.lastUpdated != @2024-06-15T08:00:00Z",

        "birthDate = birthDate",
        "contact.period.start < contact.period.end",
        "contact.period.start > contact.period.end",
        "contact.period.start = contact.period.end",
        "contact.period.start <= contact.period.end",

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
        "meta.lastUpdated = @2024-06-15T08:00:00",
        "meta.lastUpdated > @2024-06-15T08:00:00",
        "meta.lastUpdated = @2024-06-15T08:00:00.000Z",

        "extension.value = @T10:30:00",
        "extension.value != @T10:30:00",
        "extension.value < @T12:00:00",
        "extension.value > @T12:00:00",
        "extension.value = @T10:30",
        "birthDate = @T10:30:00",
        "birthDate < @T10:30:00",
        "extension.value = @1974-12-25",
        "meta.lastUpdated > @T10:30:00",
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
        "contact.extension.value = 1.5",
        "contact.extension.value < 2",
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

        "'5' + 1",
        "'5' - '1'",
        "'10' * 2",
        "'10' / 2",
        "'5' div 2",
        "'5' mod 2",
        "1 + true",
        "1 + '1'",
        "true + 1",
        "'4'.sqrt()",
        "true.exp()",
        "'4'.abs()",
        "gender + 1",
        "gender * 2",
        "active + 1",
        "1 = '1'",
        "multipleBirthInteger = '2'",
        "'5'.toInteger() + 1",

        "1 'mg' + 5",
        "5 + 1 'mg'",
        "1 'mg' - 5",
        "1 'mg' + 'x'",
        "1 'mg' + true",
        "1 'mg' * 'x'",
        "2 / 1 'mg'",
        "1 'mg' > 5",
        "5 < 1 'mg'",
        "1 'mg' = 5",
        "1 'mg' > 'x'",
        "1 'mg' > true",
        "1 'mg' > @2012-01-01",
        "1 'mg' = 'x'",
        "1 'mg' = true",
        "5 '1' = 5",
        "5 '1' > 1",
        "1 'mg' + 1 'm'",
        "1 'mg' + 1 'g'",
        "1 'mg' * 2",
        "1 'mg' / 2",

        "@2012-01-01T10:00:00Z = @2012-01-01T20:00:00+10:00",
        "(@2012-01-01T10:00:00Z | @2012-01-01T20:00:00+10:00).count()",
        "(@2012-01-01T10:00:00Z | @2012-01-01T20:00:00+10:00).distinct().count()",
        "(@2012-01-01T10:00:00Z).combine(@2012-01-01T20:00:00+10:00).distinct().count()",
        "(@2012-01-01T10:00:00Z).combine(@2012-01-01T20:00:00+10:00).isDistinct()",
        "(@2012-01-01T10:00:30).combine(@2012-01-01T10:00:30.000).distinct().count()",
        "(@2012).combine(@2012-01).distinct().count()",
        "1.combine(1.0).isDistinct()",
        "@2012-01-01T10:00:00Z in (@2012-01-01T20:00:00+10:00)",
        "(@2012-01-01T20:00:00+10:00) contains @2012-01-01T10:00:00Z",
        "(@2012-01-01T10:00:00Z).intersect(@2012-01-01T20:00:00+10:00).count()",
        "(@2012-01-01T10:00:00Z).exclude(@2012-01-01T20:00:00+10:00).count()",
        "@2012-01-01T10:00:30 = @2012-01-01T10:00:30.000",
        "(@2012-01-01T10:00:30 | @2012-01-01T10:00:30.000).distinct().count()",
        "@T10:00:30 = @T10:00:30.000",
        "(@T10:00:30 | @T10:00:30.000).distinct().count()",
        "(@2012-01-01T10:00:30.5 | @2012-01-01T10:00:30).distinct().count()",
        "(@2012-01-01T10:00:00Z | @2012-01-01T10:00:00).distinct().count()",
        "(@2012 | @2012-01).distinct().count()",
        "(@T10:00:00 | @T10:00).distinct().count()",
        "(@2012-01-01 | @2012-01-01T00:00:00Z).distinct().count()",
        "birthDate in (@1974-12-25)",
        "(birthDate | @1974-12-25).count()",
        "(meta.lastUpdated | @2024-06-15T08:00:00Z).distinct().count()",
        "(extension.value | @T10:30:00).distinct().count()",
        "birthDate is date",
        "birthDate is dateTime",
        "meta.lastUpdated is instant",
        "extension.value is time",
        "active is boolean",
        "multipleBirthInteger is integer",
        "contact.extension.value is decimal",
        "name is HumanName",
        "name.first() is HumanName",
        "name.first().family is string",
        "birthDate as date",
        "birthDate.ofType(date)",
        "name.ofType(HumanName).family",
        "birthDate.type().name",

        "birthDate.toString()",
        "meta.lastUpdated.toString()",
        "extension.value.toString()",
        "contact.extension.value.toString()",
        "multipleBirthInteger.toString()",
        "active.toString()",
        "birthDate.convertsToDate()",
        "birthDate.toDate()",
        "meta.lastUpdated.toDateTime()",
        "extension.value.toTime()",
        "contact.extension.value.toDecimal()",

        "birthDate.min()",
        "birthDate.max()",
        "contact.period.start.min()",
        "extension.value.min()",
        "name.given.min()",
        "name.given.max()",
        "birthDate.lowBoundary()",
        "birthDate.highBoundary()",
        "contact.extension.value.lowBoundary()",
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
        "contact.extension.value * 0",
        "0 / multipleBirthInteger",
        "0 / contact.extension.value",
        "missingElement * 0",
        "gender & ''",
        "'' & gender",
        "multipleBirthInteger / 1",
        "contact.extension.value / 1",
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
    /// can observe: element count, instance type, runtime value type, and value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The runtime type of <c>Value</c> is part of the signature, not decoration. A
    /// <see cref="FhirTemporal"/> and the wire string it was parsed from render to identical text,
    /// so a comparison over rendered text alone cannot see one path silently handing back an
    /// untyped string where the other hands back a typed temporal - which is the shape of the
    /// regressions this suite exists to catch.
    /// </para>
    /// <para>
    /// A thrown exception is recorded as the observed outcome rather than propagated, because "one
    /// path throws and the other returns a value" is itself a divergence this harness exists to
    /// report, and letting it escape would hide the comparison behind a stack trace.
    /// </para>
    /// <para>
    /// A green differential proves the paths agree. It does not prove they are right: two paths that
    /// share an implementation share its bugs, and this harness is structurally blind to that. Read
    /// it as a regression net over refactors, never as a correctness proof - correctness is the
    /// official suite's and the conformance tests' job.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Describe(Func<IEnumerable<IElement>> evaluate)
    {
        try
        {
            return evaluate()
                .Select(element => $"{element.InstanceType}|{ValueTypeName(element.Value)}|{Render(element.Value)}")
                .ToList();
        }
        catch (Exception ex)
        {
            return [$"threw:{ex.GetType().Name}"];
        }
    }

    private static string ValueTypeName(object? value) => value?.GetType().Name ?? "null";

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

    /// <summary>
    /// The subject every differential harness evaluates against: a real FHIR resource parsed and
    /// projected through the same <c>SchemaAwareElement</c> the server uses at runtime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This deliberately does not hand-build elements. A hand-built element supplies its own
    /// <c>InstanceType</c>, returns <c>null</c> from <c>Type</c>, and can inject an already-parsed
    /// <see cref="FhirTemporal"/> as its value - so a harness over hand-built elements never
    /// exercises schema lookup, choice-element resolution, or the wire-text-to-typed-value
    /// conversion in <c>SchemaAwareElement.ComputeValue</c>. Those are exactly the paths where
    /// resource-backed temporal regressions hide, so the harness has to run over the production
    /// element type or it is testing a mock of the thing under test.
    /// </para>
    /// <para>
    /// Element paths are constrained by what FHIR actually declares on <c>Patient</c>. Where the
    /// earlier hand-built subject invented an element, the corpus now uses the nearest real one
    /// carrying the same type: <c>meta.lastUpdated</c> for <c>instant</c>, <c>contact.period</c>
    /// for a <c>Period</c>, <c>extension.value</c> (valueTime) for <c>time</c>, and
    /// <c>contact.extension.value</c> (valueDecimal) for <c>decimal</c>.
    /// </para>
    /// </remarks>
    public static IElement CreateSubject()
    {
        return ResourceJsonNode.Parse(SubjectJson).ToElement(Schema);
    }

    private static readonly IFhirSchemaProvider Schema = FhirVersion.R5.GetSchemaProvider();

    private const string SubjectJson = """
    {
      "resourceType": "Patient",
      "id": "differential-subject",
      "meta": { "lastUpdated": "2024-06-15T08:00:00Z" },
      "extension": [
        {
          "url": "http://example.org/fhir/StructureDefinition/birth-time",
          "valueTime": "10:30:00"
        }
      ],
      "identifier": [ { "value": "abc" } ],
      "active": true,
      "name": [
        { "family": "Smith", "given": [ "John", "Q" ] },
        { "family": "Jones", "given": [ "Ann" ] }
      ],
      "telecom": [
        { "system": "phone", "value": "555-1234" },
        { "system": "email", "value": "patient@example.org" }
      ],
      "gender": "male",
      "birthDate": "1974-12-25",
      "multipleBirthInteger": 2,
      "contact": [
        {
          "extension": [
            {
              "url": "http://example.org/fhir/StructureDefinition/score",
              "valueDecimal": 1.5
            }
          ],
          "period": { "start": "2020-01-01", "end": "2021-06-15" }
        }
      ]
    }
    """;
}

/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Pins that FHIRPath literals parse the same on every host.
 */

using System.Globalization;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Shouldly;
using Xunit;

namespace Ignixa.FhirPath.Tests.Parsing;

/// <summary>
/// Guards the numeric literal parsers against the ambient culture.
/// </summary>
/// <remarks>
/// <para>
/// Both grammars read decimal and integer literals out of the token text with <c>Parse</c>. Without an
/// explicit provider that reads the host's <see cref="CultureInfo.CurrentCulture"/>, so on a host whose
/// decimal separator is a comma the literal <c>1.5</c> parses as fifteen — silently, because <c>"1.5"</c>
/// is a well-formed integer with a group separator under those rules. Nothing throws and nothing logs;
/// every expression containing a decimal literal simply computes a different answer.
/// </para>
/// <para>
/// The cultures below are chosen for what they change rather than for coverage: de-DE and fr-FR swap the
/// decimal separator and the group separator, and ar-SA additionally uses a non-ASCII decimal separator
/// (U+066B) and a U+061C-prefixed negative sign. A test that only ran on en-US would pass against the
/// unfixed parser.
/// </para>
/// </remarks>
public class LiteralParsingCultureInvarianceTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    [InlineData("en-US")]
    public void GivenADecimalLiteral_WhenTheHostCultureVaries_ThenItKeepsItsValue(string cultureName)
    {
        // 1.5 read under a comma-decimal culture becomes 15, so the sum moves from 2.5 to 16.
        UnderCulture(cultureName, () => EvaluateValue("1.5 + 1")).ShouldBe(2.5m);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    [InlineData("en-US")]
    public void GivenADecimalComparison_WhenTheHostCultureVaries_ThenTheOrderingIsUnchanged(string cultureName)
    {
        // Misparsing either side inverts this: 15 > 2 is true where 1.5 > 2 is false.
        UnderCulture(cultureName, () => EvaluateValue("1.5 > 2")).ShouldBe(false);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    [InlineData("en-US")]
    public void GivenAQuantityLiteral_WhenTheHostCultureVaries_ThenItsMagnitudeIsUnchanged(string cultureName)
    {
        // The quantity grammar has its own Parse call, distinct from the plain decimal literal's.
        UnderCulture(cultureName, () => EvaluateValue("(1.5 'mg' = 1500 'ug')")).ShouldBe(true);
    }

    private static object? EvaluateValue(string expression)
    {
        // The focus is irrelevant - every expression here is built from literals - but the evaluator
        // needs one, so a minimal resource stands in.
        //
        // The typed value is asserted, never its rendering: decimal.ToString() is itself culture-sensitive,
        // so comparing rendered text would fail on de-DE whether or not the parser was fixed, and would
        // pass on en-US whether or not it was broken. That is the trap this file exists to close.
        var focus = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"p1"}""").ToElement(Schema);
        var result = focus.Select(expression).ToList();
        result.Count.ShouldBe(1, $"'{expression}' should yield exactly one result");
        return result[0].Value;
    }

    private static readonly R4CoreSchemaProvider Schema = new();

    private static T UnderCulture<T>(string cultureName, Func<T> act)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            return act();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}

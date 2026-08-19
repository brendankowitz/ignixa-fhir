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
/// <para>
/// <em>Every row uses a different expression, and that is load-bearing.</em>
/// <c>TypedElementExtensions</c> caches parsed ASTs in a process-wide store keyed on the expression text
/// alone. An earlier revision of this file passed the same expression on all four rows, so the parse
/// happened exactly once — under whichever culture xUnit scheduled first — and the other three rows read
/// a cached AST without reaching a <c>Parse</c> call at all. The commit that added it claimed nine of
/// twelve rows failed without the fix; at most three could, and had any other test in the assembly
/// parsed <c>"1.5 + 1"</c> first the file would have been entirely vacuous — its 9-of-12 was an
/// aggregate that happened to look right over three quarters inert.
/// </para>
/// <para>
/// Re-measured against parsers with the invariant provider removed again, on this file: each of de-DE,
/// fr-FR and ar-SA fails all four of its literal expressions and en-US passes all four, whether the
/// rows are run one culture at a time or all together — 12 of the 16 literal rows either way, which is
/// what "no row rides another row's parse" looks like. Distinct expressions are preferred over clearing
/// the cache in the arrange because they need no global mutation and so cannot race another test.
/// </para>
/// </remarks>
public class LiteralParsingCultureInvarianceTests
{
    [Theory]
    [InlineData("de-DE", "1.5 + 1", 2.5)]
    [InlineData("fr-FR", "2.5 + 1", 3.5)]
    [InlineData("ar-SA", "3.5 + 1", 4.5)]
    [InlineData("en-US", "4.5 + 1", 5.5)]
    public void GivenADecimalLiteral_WhenTheHostCultureVaries_ThenItKeepsItsValue(
        string cultureName,
        string expression,
        double expected)
    {
        // 1.5 read under a comma-decimal culture becomes 15, so the sum moves from 2.5 to 16.
        UnderCulture(cultureName, () => EvaluateValue(expression)).ShouldBe((decimal)expected);
    }

    [Theory]
    [InlineData("de-DE", "1.5 > 9")]
    [InlineData("fr-FR", "2.5 > 9")]
    [InlineData("ar-SA", "3.5 > 9")]
    [InlineData("en-US", "4.5 > 9")]
    public void GivenADecimalComparison_WhenTheHostCultureVaries_ThenTheOrderingIsUnchanged(
        string cultureName,
        string expression)
    {
        // Misparsing the left operand inverts this: 15 > 9 is true where 1.5 > 9 is false. The right
        // operand is an integer literal so that only one side of the comparison can move.
        UnderCulture(cultureName, () => EvaluateValue(expression)).ShouldBe(false);
    }

    [Theory]
    [InlineData("de-DE", "(1.5 'mg' = 1500 'ug')")]
    [InlineData("fr-FR", "(2.5 'mg' = 2500 'ug')")]
    [InlineData("ar-SA", "(3.5 'mg' = 3500 'ug')")]
    [InlineData("en-US", "(4.5 'mg' = 4500 'ug')")]
    public void GivenAQuantityLiteral_WhenTheHostCultureVaries_ThenItsMagnitudeIsUnchanged(
        string cultureName,
        string expression)
    {
        // The quantity grammar has its own Parse call, distinct from the plain decimal literal's.
        UnderCulture(cultureName, () => EvaluateValue(expression)).ShouldBe(true);
    }

    /// <summary>
    /// The calendar-duration grammar is the fourth <c>Parse</c> call each grammar makes and the one this
    /// file did not reach. It is a separate parser from the quantity one because a keyword unit is not a
    /// quoted UCUM code, so fixing one says nothing about the other.
    /// </summary>
    /// <remarks>
    /// The right operand is written in hours, and as an integer, so that a misparsed left operand cannot
    /// be cancelled out by the same misparse on the right: <c>1.5 weeks</c> is 252 hours, and the fifteen
    /// weeks a comma-decimal host would read is 2520.
    /// </remarks>
    [Theory]
    [InlineData("de-DE", "(1.5 weeks = 252 hours)")]
    [InlineData("fr-FR", "(2.5 weeks = 420 hours)")]
    [InlineData("ar-SA", "(3.5 weeks = 588 hours)")]
    [InlineData("en-US", "(4.5 weeks = 756 hours)")]
    public void GivenACalendarDurationLiteral_WhenTheHostCultureVaries_ThenItsMagnitudeIsUnchanged(
        string cultureName,
        string expression)
    {
        UnderCulture(cultureName, () => EvaluateValue(expression)).ShouldBe(true);
    }

    /// <summary>
    /// The UCUM table behind every quantity comparison is loaded from a static field, and it parses its
    /// own exponents - plain ASCII <c>-1</c> and the like - under the ambient culture. Under ar-SA, whose
    /// negative sign is U+061C U+002D, that threw, and because it threw inside a type initializer the
    /// runtime cached the failure: <c>ValueOrdering</c> was then dead for the rest of the process, on
    /// every culture, taking every quantity comparison, sort, aggregate and equality with it.
    /// </summary>
    /// <remarks>
    /// This is separate from the quantity rows above because it is about the first touch rather than
    /// about the arithmetic. It was invisible while all rows shared one expression: the AST cache meant
    /// ar-SA usually never reached a parser, let alone the converter.
    /// </remarks>
    [Theory]
    [InlineData("ar-SA")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void GivenAHostWithANonAsciiNegativeSign_WhenTheUnitTableIsFirstTouched_ThenItStillLoads(
        string cultureName)
    {
        // Act - the constructor, deliberately, not the Instance singleton. Instance runs its type
        // initializer once per process, so by the time this row executes some other test has already
        // initialized it under whatever culture that test ran in, and the row passes whether or not the
        // load is culture-safe. Measured: against the unfixed loader this whole class passes 20 of 20,
        // while these four rows in isolation fail 4 of 4. Constructing directly re-runs the load every
        // time, which is what makes the guard order-independent and therefore able to fail in CI.
        var converter = UnderCulture(cultureName, () => new Ignixa.FhirPath.Types.QuantityUnitConverter());

        // Assert
        converter.ShouldNotBeNull();
        UnderCulture(cultureName, () => converter.Convert(1m, "mg", "g")).ShouldBe(0.001m);
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

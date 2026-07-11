// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Application.Tests.Search.Expressions.Parsers.Legacy;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

/// <summary>
/// Differential characterization suite for PR #332 (the handwritten-scanner search parser rewrite):
/// runs the SAME input corpus through the pre-rewrite parser (Legacy/, a frozen snapshot of `main`
/// before this PR) and the current parser, side by side, and asserts they produce equivalent
/// results.
///
/// Two kinds of assertions:
/// - <see cref="AssertIdenticalBehavior"/>: old and new MUST produce the same expression tree (via
///   <see cref="Expression.ToString"/>, which every Expression subtype overrides with a full
///   recursive structural rendering - see Expression.cs/ChainedExpression.cs/etc.) or the same
///   exception type. A failure here is either a genuine regression or a new, undocumented behavior
///   change that needs triage.
/// - <see cref="AssertDocumentedDivergence"/>: old and new intentionally differ. Both behaviors are
///   pinned explicitly so the divergence can't silently change shape again, and the reason is
///   recorded in the test body (mirrors the "Behavior changes" section of the PR description).
///
/// This suite exists because a rewrite's entire value proposition rests on "same behavior, cleaner
/// structure" - see docs/features/search/investigations/superpower-search-expression-parser.md. When
/// the old parser is eventually deleted, the Legacy/ snapshot can be deleted too; this suite (and the
/// documented-divergence list) is the durable record of what changed and why.
/// </summary>
public class SearchParserOldVsNewParityTests
{
    private static readonly string[] Patient = ["Patient"];
    private static readonly string[] Observation = ["Observation"];

    private static IExpressionParser BuildOldParser(SearchParserTestContext context)
    {
        var legacyValueParser = new LegacySearchParameterExpressionParser(
            new ReferenceSearchValueParser(context.SchemaProvider),
            context.SchemaProvider);

        return new LegacyExpressionParser(() => context.DefinitionManager, legacyValueParser, context.SchemaProvider);
    }

    private static (Expression? Expression, Exception? Error) TryParse(IExpressionParser parser, string[] resourceTypes, string key, string value)
    {
        try
        {
            return (parser.Parse(resourceTypes, key, value), null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    private static (Expression? Expression, Exception? Error) TryParseInclude(IExpressionParser parser, string[] resourceTypes, string includeValue, bool isReversed, bool iterate)
    {
        try
        {
            return (parser.ParseInclude(resourceTypes, includeValue, isReversed, iterate), null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    /// <summary>
    /// Old and new parser MUST agree: same success shape, or the same exception type.
    /// </summary>
    private static void AssertIdenticalBehavior(SearchParserTestContext context, string[] resourceTypes, string key, string value)
    {
        var oldParser = BuildOldParser(context);

        var (oldExpression, oldError) = TryParse(oldParser, resourceTypes, key, value);
        var (newExpression, newError) = TryParse(context.Parser, resourceTypes, key, value);

        if (oldError is not null || newError is not null)
        {
            oldError.ShouldNotBeNull($"old parser succeeded (returned '{oldExpression}') but new parser threw {newError?.GetType().Name}: {newError?.Message}");
            newError.ShouldNotBeNull($"new parser succeeded (returned '{newExpression}') but old parser threw {oldError?.GetType().Name}: {oldError?.Message}");
            newError!.GetType().ShouldBe(oldError!.GetType(), $"exception type diverged for key='{key}' value='{value}'. Old: {oldError.GetType().Name} '{oldError.Message}'. New: {newError.GetType().Name} '{newError.Message}'.");
            return;
        }

        newExpression!.ToString().ShouldBe(oldExpression!.ToString(), $"expression shape diverged for key='{key}' value='{value}'");
    }

    private static void AssertIdenticalIncludeBehavior(SearchParserTestContext context, string[] resourceTypes, string includeValue, bool isReversed, bool iterate)
    {
        var oldParser = BuildOldParser(context);

        var (oldExpression, oldError) = TryParseInclude(oldParser, resourceTypes, includeValue, isReversed, iterate);
        var (newExpression, newError) = TryParseInclude(context.Parser, resourceTypes, includeValue, isReversed, iterate);

        if (oldError is not null || newError is not null)
        {
            oldError.ShouldNotBeNull($"old parser succeeded but new parser threw {newError?.GetType().Name}: {newError?.Message}");
            newError.ShouldNotBeNull($"new parser succeeded but old parser threw {oldError?.GetType().Name}: {oldError?.Message}");
            newError!.GetType().ShouldBe(oldError!.GetType(), $"exception type diverged for includeValue='{includeValue}'. Old: {oldError.GetType().Name} '{oldError.Message}'. New: {newError.GetType().Name} '{newError.Message}'.");
            return;
        }

        newExpression!.ToString().ShouldBe(oldExpression!.ToString(), $"include expression shape diverged for includeValue='{includeValue}'");
    }

    #region Identical behavior: simple parameters, all types

    [Fact]
    public void GivenStringParam_WhenParsingPlainValue_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);

        AssertIdenticalBehavior(context, Patient, "name", "Smith");
    }

    [Theory]
    [InlineData("exact")]
    [InlineData("contains")]
    public void GivenStringParam_WhenParsingWithModifier_ThenIdenticalToOldParser(string modifier)
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);

        AssertIdenticalBehavior(context, Patient, $"name:{modifier}", "Smith");
    }

    [Fact]
    public void GivenTokenParam_WhenParsingSystemPipeCode_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "code", SearchParamType.Token);

        AssertIdenticalBehavior(context, Observation, "code", "http://loinc.org|1234-5");
    }

    [Fact]
    public void GivenTokenParam_WhenParsingWithNotModifier_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "code", SearchParamType.Token);

        AssertIdenticalBehavior(context, Observation, "code:not", "http://loinc.org|1234-5");
    }

    [Theory]
    [InlineData("eq")]
    [InlineData("ne")]
    [InlineData("gt")]
    [InlineData("lt")]
    [InlineData("ge")]
    [InlineData("le")]
    [InlineData("sa")]
    [InlineData("eb")]
    public void GivenDateParam_WhenParsingWithComparatorPrefix_ThenIdenticalToOldParser(string comparator)
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "birthdate", SearchParamType.Date);

        AssertIdenticalBehavior(context, Patient, "birthdate", $"{comparator}2020-01-01");
    }

    /// <summary>
    /// "ap" (approximately) computes its bounds from DateTimeOffset.UtcNow, so the old and new
    /// parser calls - however close in wall-clock time - never produce byte-identical timestamps.
    /// Compares structural shape (field names/operators) with timestamps normalized out, rather than
    /// the raw ToString(), to avoid a test that's flaky by construction.
    /// </summary>
    [Fact]
    public void GivenDateParam_WhenParsingWithApComparator_ThenSameShapeAsOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "birthdate", SearchParamType.Date);
        var oldParser = BuildOldParser(context);

        var (oldExpression, oldError) = TryParse(oldParser, Patient, "birthdate", "ap2020-01-01");
        var (newExpression, newError) = TryParse(context.Parser, Patient, "birthdate", "ap2020-01-01");

        oldError.ShouldBeNull();
        newError.ShouldBeNull();

        static string NormalizeTimestamps(string s) =>
            System.Text.RegularExpressions.Regex.Replace(s, @"\d{4}-\d{2}-\d{2}T[\d:.]+\+\d{2}:\d{2}", "<timestamp>");

        NormalizeTimestamps(newExpression!.ToString()).ShouldBe(NormalizeTimestamps(oldExpression!.ToString()));
    }

    [Fact]
    public void GivenNumberParam_WhenParsingPlainValue_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "value-string", SearchParamType.Number);

        AssertIdenticalBehavior(context, Observation, "value-string", "gt5.4");
    }

    [Fact]
    public void GivenQuantityParam_WhenParsingSystemAndCode_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "value-quantity", SearchParamType.Quantity);

        AssertIdenticalBehavior(context, Observation, "value-quantity", "5.4|http://unitsofmeasure.org|mg");
    }

    [Fact]
    public void GivenUriParam_WhenParsingPlainValue_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "url", SearchParamType.Uri);

        AssertIdenticalBehavior(context, Patient, "url", "http://example.org/fhir/Patient/123");
    }

    [Theory]
    [InlineData("above")]
    [InlineData("below")]
    public void GivenUriParam_WhenParsingWithAboveBelowModifier_ThenIdenticalToOldParser(string modifier)
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "url", SearchParamType.Uri);

        AssertIdenticalBehavior(context, Patient, $"url:{modifier}", "http://example.org/fhir");
    }

    [Fact]
    public void GivenReferenceParam_WhenParsingRelativeReference_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "patient", SearchParamType.Reference, targets: Patient);

        AssertIdenticalBehavior(context, Observation, "patient", "Patient/123");
    }

    [Fact]
    public void GivenReferenceParam_WhenParsingWithTypeModifier_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, targets: [.. Patient, "Group"]);

        AssertIdenticalBehavior(context, Observation, "subject:Patient", "123");
    }

    #endregion

    #region Identical behavior: :missing, :text, comma-alternatives, escaping

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void GivenAnyParam_WhenParsingMissingModifier_ThenIdenticalToOldParser(string missingValue)
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);

        AssertIdenticalBehavior(context, Patient, "name:missing", missingValue);
    }

    [Fact]
    public void GivenTokenParam_WhenParsingTextModifier_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "code", SearchParamType.Token);

        AssertIdenticalBehavior(context, Observation, "code:text", "diabetes");
    }

    [Fact]
    public void GivenStringParam_WhenParsingCommaAlternatives_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);

        AssertIdenticalBehavior(context, Patient, "name", "Smith,Jones,Doe");
    }

    [Fact]
    public void GivenTokenParam_WhenParsingEscapedCommaInValue_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "code", SearchParamType.Token);

        AssertIdenticalBehavior(context, Observation, "code", @"http://example.org|a\,b");
    }

    [Fact]
    public void GivenTokenParam_WhenParsingEscapedPipeInValue_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "code", SearchParamType.Token);

        AssertIdenticalBehavior(context, Observation, "code", @"a\|b");
    }

    [Fact]
    public void GivenCompositeParam_WhenParsingEscapedDollarInComponent_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        var codeParam = context.Add("Observation", "combo-code", SearchParamType.Token);
        var valueParam = context.Add("Observation", "combo-value-quantity", SearchParamType.Quantity);
        var composite = context.Add(
            "Observation",
            "code-value-quantity",
            SearchParamType.Composite,
            components:
            [
                new SearchParameterComponentInfo(codeParam.Url) { ResolvedSearchParameter = codeParam },
                new SearchParameterComponentInfo(valueParam.Url) { ResolvedSearchParameter = valueParam }
            ]);

        AssertIdenticalBehavior(context, Observation, composite.Code, @"http://loinc.org|1234-5\$extra$5.4|http://unitsofmeasure.org|mg");
    }

    #endregion

    #region Identical behavior: chains, _has, _not-referenced

    [Fact]
    public void GivenForwardChain_WhenParsingUntypedSingleTarget_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "patient", SearchParamType.Reference, targets: Patient);
        context.Add("Patient", "name", SearchParamType.String);

        AssertIdenticalBehavior(context, Observation, "patient.name", "Smith");
    }

    [Fact]
    public void GivenForwardChain_WhenParsingTypedTarget_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, targets: [.. Patient, "Group"]);
        context.Add("Patient", "name", SearchParamType.String);
        context.Add("Group", "name", SearchParamType.String);

        AssertIdenticalBehavior(context, Observation, "subject:Patient.name", "Smith");
    }

    [Fact]
    public void GivenReverseChain_WhenParsingHasKey_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "patient", SearchParamType.Reference, targets: Patient);
        context.Add("Observation", "code", SearchParamType.Token);

        AssertIdenticalBehavior(context, Patient, "_has:Observation:patient:code", "http://loinc.org|1234-5");
    }

    [Fact]
    public void GivenNestedReverseChain_WhenParsingTypedForwardThenHasThenNested_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "patient", SearchParamType.Reference, targets: Patient);
        context.Add("Group", "member", SearchParamType.Reference, targets: Patient);
        context.Add("Group", "_tag", SearchParamType.Token);

        AssertIdenticalBehavior(context, Observation, "patient:Patient._has:Group:member:_tag", "http://example.org/tags|reviewed");
    }

    [Theory]
    [InlineData("*:*")]
    [InlineData("Observation:*")]
    [InlineData("Observation:subject")]
    public void GivenNotReferenced_WhenParsingVariousForms_ThenIdenticalToOldParser(string value)
    {
        var context = new SearchParserTestContext();

        AssertIdenticalBehavior(context, Observation, "_not-referenced", value);
    }

    #endregion

    #region Identical behavior: include/revinclude

    [Fact]
    public void GivenInclude_WhenParsingExplicitTargetParam_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, targets: Patient);

        AssertIdenticalIncludeBehavior(context, Observation, "Observation:subject", isReversed: false, iterate: false);
    }

    [Fact]
    public void GivenInclude_WhenParsingWithExplicitResourceType_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, targets: [.. Patient, "Group"]);

        AssertIdenticalIncludeBehavior(context, Observation, "Observation:subject:Patient", isReversed: false, iterate: false);
    }

    [Fact]
    public void GivenRevInclude_WhenParsingDefaultTarget_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, targets: Patient);

        AssertIdenticalIncludeBehavior(context, Patient, "Observation:subject", isReversed: true, iterate: false);
    }

    [Fact]
    public void GivenIncludeWildcard_WhenParsingStar_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, targets: Patient);
        context.Add("Observation", "encounter", SearchParamType.Reference, targets: ["Encounter"]);

        AssertIdenticalIncludeBehavior(context, Observation, "*", isReversed: false, iterate: false);
    }

    [Fact]
    public void GivenIncludeIterate_WhenParsingIterateFlagSet_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, targets: Patient);

        AssertIdenticalIncludeBehavior(context, Observation, "Observation:subject", isReversed: false, iterate: true);
    }

    #endregion

    #region Identical behavior: error paths not touched by this PR's documented divergences

    [Fact]
    public void GivenMissingModifier_WhenParsingNonBooleanValue_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);

        AssertIdenticalBehavior(context, Patient, "name:missing", "yes");
    }

    [Fact]
    public void GivenModifierNotSupportedForParamType_WhenParsingTextOnStringParam_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);

        AssertIdenticalBehavior(context, Patient, "name:text", "Smith");
    }

    [Fact]
    public void GivenUnsupportedSearchParameter_WhenParsingUnknownCode_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();

        AssertIdenticalBehavior(context, Patient, "totally-unknown-code", "value");
    }

    [Fact]
    public void GivenChainOnNonReferenceParam_WhenParsingDottedString_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);

        AssertIdenticalBehavior(context, Patient, "name.family", "Smith");
    }

    [Fact]
    public void GivenForwardChain_WhenTargetTypeNotSupportedForParam_ThenIdenticalToOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, targets: Patient);

        AssertIdenticalBehavior(context, Observation, "subject:Encounter.status", "arrived");
    }

    #endregion

    #region Documented divergences: intentional behavior changes (see PR description "Behavior changes")

    /// <summary>
    /// R4 escape rule: only \, \$ \| \\ are legal escapes. Old parser passed a stray backslash
    /// through as literal text (permissive). New parser rejects it with a positioned syntax error
    /// (spec-correct tightening). This is the escape-strictness change called out in the PR
    /// description; pinned here so it can't silently revert or drift to a third behavior.
    /// </summary>
    [Fact]
    public void GivenInvalidEscape_WhenParsing_ThenNewParserRejectsWhereOldParserWasPermissive()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);
        var oldParser = BuildOldParser(context);

        var (oldExpression, oldError) = TryParse(oldParser, Patient, "name", @"a\xb");
        oldError.ShouldBeNull("documenting old (pre-PR) behavior: old parser accepted the invalid escape");
        oldExpression.ShouldNotBeNull();

        var (_, newError) = TryParse(context.Parser, Patient, "name", @"a\xb");
        newError.ShouldNotBeNull("new parser must reject invalid escapes with a positioned syntax error");
        newError.ShouldBeOfType<Ignixa.Search.Indexing.InvalidSearchOperationException>();
    }

    /// <summary>
    /// Old parser recognized the literal ":type" modifier as SearchModifierCode.Type with no
    /// resource-type text attached (SearchParamModifierMapping matches "type" as an enum literal
    /// before the "modifier names a target resource type" branch is even considered). Given a bare
    /// reference id (no "ResourceType/" prefix), that half-built modifier silently rebuilds the
    /// reference with ResourceType = null - i.e. the ":type" constraint is recognized but silently
    /// has NO effect, rather than erroring or actually restricting the type. New parser's modifier
    /// table explicitly excludes SearchModifierCode.Type from literal matching (see
    /// SearchKeyBinder.cs: `.Where(code => code != SearchModifierCode.Type)`), so "type" falls
    /// through to be interpreted as a target-resource-type restriction naming a resource type
    /// literally called "type", which doesn't exist - a real, reported error. Documented
    /// improvement (silent no-op -> explicit rejection), not a regression - pinned so it can't
    /// silently revert to the old no-op behavior.
    /// </summary>
    [Fact]
    public void GivenLiteralTypeModifier_WhenParsing_ThenNewParserRejectsWhereOldParserSilentlyDroppedTheConstraint()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, targets: Patient);
        var oldParser = BuildOldParser(context);

        var (oldExpression, oldError) = TryParse(oldParser, Observation, "subject:type", "123");
        oldError.ShouldBeNull("documenting old (pre-PR) behavior: old parser accepted the literal ':type' modifier on a bare reference id");
        oldExpression.ShouldNotBeNull();

        var (_, newError) = TryParse(context.Parser, Observation, "subject:type", "123");
        newError.ShouldNotBeNull("new parser must reject the literal ':type' modifier");
    }

    /// <summary>
    /// Old parser let a raw FormatException escape BuildOfTypeExpression on a malformed :of-type
    /// value. New parser wraps it in BadSearchRequestException (proper 400 categorization instead
    /// of an unhandled framework exception type). Documented improvement.
    /// </summary>
    [Fact]
    public void GivenMalformedOfTypeValue_WhenParsing_ThenNewParserWrapsWhereOldParserLeakedFormatException()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "identifier", SearchParamType.Token);
        var oldParser = BuildOldParser(context);

        var (_, oldError) = TryParse(oldParser, Patient, "identifier:of-type", "|MR|");
        oldError.ShouldNotBeNull();
        oldError.ShouldBeOfType<FormatException>("documenting old (pre-PR) behavior: a raw FormatException escaped");

        var (_, newError) = TryParse(context.Parser, Patient, "identifier:of-type", "|MR|");
        newError.ShouldNotBeNull();
        newError.ShouldBeOfType<Ignixa.Search.Exceptions.BadSearchRequestException>("new parser must categorize this as a bad request, not leak a raw FormatException");
    }

    /// <summary>
    /// Incomplete `_has` key (missing the trailing reference-parameter segment). Old parser raised a
    /// specific ReverseChainMissingReference resource message; new parser's scanner reports a
    /// generic positioned syntax error instead. Both are legitimate 400s; the message resource is no
    /// longer reachable via this path (Resources.ReverseChainMissingReference is now orphaned).
    /// Documented so a future cleanup pass knows this resource string is dead, not load-bearing.
    /// </summary>
    [Fact]
    public void GivenIncompleteHasKeyMissingReferenceParam_WhenParsing_ThenErrorShapeDivergesFromOldParser()
    {
        var context = new SearchParserTestContext();
        var oldParser = BuildOldParser(context);

        var (_, oldError) = TryParse(oldParser, Observation, "_has:Observation:patient", "value");
        oldError.ShouldNotBeNull();
        oldError.ShouldBeOfType<Ignixa.Search.Indexing.InvalidSearchOperationException>();
        oldError!.Message.ShouldBe(Ignixa.Search.Resources.ReverseChainMissingReference);

        var (_, newError) = TryParse(context.Parser, Observation, "_has:Observation:patient", "value");
        newError.ShouldNotBeNull();
        newError!.Message.ShouldNotBe(oldError.Message, "new parser's message shape has changed for this input (positioned syntax error, not the old resource string) - documented, not a regression");
    }

    /// <summary>
    /// date=2015,gt2016 (comma-alternatives combined with a comparator prefix). Old parser rejected
    /// this as a date-format BadSearchRequestException (it tried to parse "2015,gt2016" as one date
    /// literal since comparator detection happens before comma-splitting only for the FIRST
    /// alternative in some code paths). New parser raises SearchComparatorNotSupportedException,
    /// reflecting that a non-eq comparator combined with multiple comma values is itself invalid,
    /// independent of whether either date parses. Both 400s; exception TYPE differs, so this is a
    /// documented divergence rather than an AssertIdenticalBehavior case.
    /// </summary>
    [Fact]
    public void GivenCommaAlternativesWithComparatorPrefix_WhenParsingDate_ThenErrorTypeDivergesFromOldParser()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "birthdate", SearchParamType.Date);
        var oldParser = BuildOldParser(context);

        var (_, oldError) = TryParse(oldParser, Patient, "birthdate", "2015,gt2016");
        oldError.ShouldNotBeNull("documenting old (pre-PR) behavior");

        var (_, newError) = TryParse(context.Parser, Patient, "birthdate", "2015,gt2016");
        newError.ShouldNotBeNull();
        newError.ShouldBeOfType<Ignixa.Search.Indexing.InvalidSearchOperationException>();
        newError!.Message.ShouldBe(Ignixa.Search.Resources.SearchComparatorNotSupported);
    }

    #endregion
}

using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Lowering.Composite;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

/// <summary>
/// Every composite rule now threads its own code-overflow column into <c>TokenColumnEquality</c>, so an
/// overflowed token code has to be matched against a (prefix, remainder) pair on each composite table —
/// with the slot-correct column names. A rule that passed the wrong column, or none at all, would still
/// build a plausible predicate and silently never match; only naming the expected pair per slot catches it.
/// </summary>
public class CompositeTokenCodeOverflowTests
{
    private const int SplitWidth = 128;

    private static readonly string OverflowedCode = new string('A', SplitWidth) + new string('B', 30);

    private static SearchParameterInfo Composite()
        => new("composite", "composite", SearchParamType.Composite, new Uri("http://example.org/fhir/SearchParameter/Observation-composite"));

    private static SearchParameterInfo Component(string code, SearchParamType type)
        => new(code, code, type, new Uri($"http://example.org/fhir/SearchParameter/Observation-{code}"));

    private static LeafContext Context()
        => new(new SymbolTable(
            new Dictionary<string, short> { [Composite().Url!.ToString()] = 302 },
            new Dictionary<string, short> { ["Observation"] = 104, ["DocumentReference"] = 55 }));

    private static SearchParameterPredicateExpression Token(string code, string? tokenCode)
        => new(Component(code, SearchParamType.Token), SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, tokenCode, text: null));

    public static TheoryData<string, Func<Predicate>, string, string> OverflowedSlots() => new()
    {
        {
            "TokenDateTime slot 1", () => TokenDateTimeLoweringRule.Lower(
                Composite(),
                [
                    Token("code", OverflowedCode),
                    new(Component("date", SearchParamType.Date), SearchComparator.Eq, modifier: null,
                        new DateTimeSearchValue(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero))),
                ],
                Context(), 104).Predicate!,
            "Code1", "CodeOverflow1"
        },
        {
            "TokenNumberNumber slot 1", () => TokenNumberNumberLoweringRule.Lower(
                Composite(),
                [
                    Token("code", OverflowedCode),
                    new(Component("low", SearchParamType.Number), SearchComparator.Ge, modifier: null, new NumberSearchValue(1m)),
                    new(Component("high", SearchParamType.Number), SearchComparator.Le, modifier: null, new NumberSearchValue(9m)),
                ],
                Context(), 104).Predicate!,
            "Code1", "CodeOverflow1"
        },
        {
            "TokenQuantity slot 1", () => TokenQuantityLoweringRule.Lower(
                Composite(),
                [
                    Token("code", OverflowedCode),
                    new(Component("value-quantity", SearchParamType.Quantity), SearchComparator.Eq, modifier: null,
                        new QuantitySearchValue(system: null, code: null, 5.4m)),
                ],
                Context(), 104).Predicate!,
            "Code1", "CodeOverflow1"
        },
        {
            "TokenString slot 1", () => TokenStringLoweringRule.Lower(
                Composite(),
                [
                    Token("code", OverflowedCode),
                    new(Component("value-string", SearchParamType.String), SearchComparator.Eq, modifier: null, new StringSearchValue("Smith")),
                ],
                Context(), 104).Predicate!,
            "Code1", "CodeOverflow1"
        },
        {
            "TokenToken slot 1", () => TokenTokenLoweringRule.Lower(
                Composite(),
                [Token("code", OverflowedCode), Token("value-concept", "high")],
                Context(), 104).Predicate!,
            "Code1", "CodeOverflow1"
        },
        {
            "TokenToken slot 2", () => TokenTokenLoweringRule.Lower(
                Composite(),
                [Token("code", "8480-6"), Token("value-concept", OverflowedCode)],
                Context(), 104).Predicate!,
            "Code2", "CodeOverflow2"
        },
        {
            "ReferenceToken slot 2", () => ReferenceTokenLoweringRule.Lower(
                Composite(),
                [
                    new(Component("target", SearchParamType.Reference), SearchComparator.Eq, modifier: null,
                        new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "DocumentReference", resourceId: "456")),
                    Token("code", OverflowedCode),
                ],
                Context(), 104).Predicate!,
            "Code2", "CodeOverflow2"
        },
    };

    [Theory]
    [MemberData(nameof(OverflowedSlots))]
    public void GivenAnOverflowedTokenCodeInACompositeSlot_WhenLowered_ThenComparesBothHalvesAgainstThatSlotsColumns(
        string scenario, Func<Predicate> lower, string codeColumn, string overflowColumn)
    {
        // Act
        var predicate = lower();

        // Assert — the prefix goes to the slot's Code column and the remainder to its CodeOverflow column
        var expectedPrefix = OverflowedCode[..SplitWidth];
        var expectedRemainder = OverflowedCode[SplitWidth..];
        var comparisons = Flatten(predicate).OfType<Predicate.Equal>().ToList();
        comparisons.ShouldContain(
            e => e.Column.Column == codeColumn && Equals(e.Value.Value, expectedPrefix),
            $"{scenario}: no equality on {codeColumn} against the 128-char prefix");
        comparisons.ShouldContain(
            e => e.Column.Column == overflowColumn && Equals(e.Value.Value, expectedRemainder),
            $"{scenario}: no equality on {overflowColumn} against the remainder");

        // Assert — and never the pre-fix behaviour: the whole code compared against the inline column,
        // which the row generator truncated, so no row could ever match.
        var wholeCode = OverflowedCode;
        comparisons.ShouldNotContain(
            e => Equals(e.Value.Value, wholeCode),
            $"{scenario}: the whole code was compared against a single column");
    }

    private static readonly string ExactlyInlineWidthCode = new('A', SplitWidth);

    // Exactly 128 characters is the boundary the >128 cases above never reach. It needs the
    // "CodeOverflow IS NULL" guard: a stored code of 200 characters writes its first 128 into the Code
    // column, so without the guard a search for those 128 characters false-positive matches it. Testing
    // only >128 leaves the one case where equality alone is wrong uncovered.
    public static TheoryData<string, Func<Predicate>, string, string> ExactlyInlineWidthSlots() => new()
    {
        {
            "TokenToken slot 1", () => TokenTokenLoweringRule.Lower(
                Composite(),
                [Token("code", ExactlyInlineWidthCode), Token("value-concept", "high")],
                Context(), 104).Predicate!,
            "Code1", "CodeOverflow1"
        },
        {
            "TokenToken slot 2", () => TokenTokenLoweringRule.Lower(
                Composite(),
                [Token("code", "8480-6"), Token("value-concept", ExactlyInlineWidthCode)],
                Context(), 104).Predicate!,
            "Code2", "CodeOverflow2"
        },
        {
            "TokenQuantity slot 1", () => TokenQuantityLoweringRule.Lower(
                Composite(),
                [
                    Token("code", ExactlyInlineWidthCode),
                    new(Component("value-quantity", SearchParamType.Quantity), SearchComparator.Eq, modifier: null,
                        new QuantitySearchValue(system: null, code: null, 5.4m)),
                ],
                Context(), 104).Predicate!,
            "Code1", "CodeOverflow1"
        },
        {
            "ReferenceToken slot 2", () => ReferenceTokenLoweringRule.Lower(
                Composite(),
                [
                    new(Component("target", SearchParamType.Reference), SearchComparator.Eq, modifier: null,
                        new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "DocumentReference", resourceId: "456")),
                    Token("code", ExactlyInlineWidthCode),
                ],
                Context(), 104).Predicate!,
            "Code2", "CodeOverflow2"
        },
    };

    [Theory]
    [MemberData(nameof(ExactlyInlineWidthSlots))]
    public void GivenATokenCodeOfExactlyTheInlineWidth_WhenLowered_ThenGuardsAgainstMatchingATruncatedLongerCode(
        string scenario, Func<Predicate> lower, string codeColumn, string overflowColumn)
    {
        // Act
        var predicate = lower();

        // Assert — the whole code goes to the Code column, with an IS NULL on the overflow column
        var terms = Flatten(predicate).ToList();
        terms.OfType<Predicate.Equal>().ShouldContain(
            e => e.Column.Column == codeColumn && Equals(e.Value.Value, ExactlyInlineWidthCode),
            $"{scenario}: no equality on {codeColumn} against the full 128-char code");
        terms.OfType<Predicate.IsNull>().ShouldContain(
            n => n.Column.Column == overflowColumn,
            $"{scenario}: no '{overflowColumn} IS NULL' guard, so a truncated longer code would match");
    }

    private static IEnumerable<Predicate> Flatten(Predicate predicate)
    {
        yield return predicate;

        IReadOnlyList<Predicate> children = predicate switch
        {
            Predicate.And and => [and.Left, and.Right],
            Predicate.Or or => [or.Left, or.Right],
            _ => [],
        };

        foreach (var child in children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }
}

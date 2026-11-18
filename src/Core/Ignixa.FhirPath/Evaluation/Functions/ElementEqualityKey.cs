// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using System.Globalization;
using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// Computes a hash consistent with <see cref="FunctionHelpers.AreElementsEqual(IElement?, IElement?)"/> for the
/// elements that have one.
/// </summary>
/// <remarks>
/// <para>
/// The contract is one-directional: whenever <see cref="FunctionHelpers.AreElementsEqual(IElement?, IElement?)"/>
/// holds for two keyed elements, their keys are equal. Unequal keys therefore prove inequality and let a
/// collection operation skip the comparison; equal keys prove nothing and the comparison still runs. That is all
/// <see cref="ElementEqualityIndex"/> needs, and it is why a key may be coarse.
/// </para>
/// <para>
/// <b>Top level.</b> Strings (and anything else with a wire string) are keyed by their text, numbers by their
/// <see cref="decimal"/> value - whose hash is equal for equal values whatever the scale, so <c>1</c>, <c>1.0</c>
/// and <c>1.00</c> share a key - Booleans by value, and complex elements by their children (below). Temporals and
/// quantities get no key: <see cref="TemporalOperand.AreSameItem"/> equates different literals and a temporal also
/// equals a String with its literal's text, and a quantity equals another after unit conversion and equals a bare
/// number in unit <c>'1'</c>. An unkeyed element is compared against every other, exactly as before #533.
/// </para>
/// <para>
/// <b>Inside a complex element</b> there is no "compared against every other": the child's contribution must be
/// equal for every pair of children the comparison could find equal. Where FHIRPath equality reaches across kinds,
/// every kind it reaches gets the same constant:
/// </para>
/// <list type="bullet">
///   <item><description>
///   Quantities, numbers, and a complex element with only a quantity's child names whose <c>value</c> could be a
///   magnitude share <see cref="QuantityLikeTag"/>: a quantity equals a number in unit <c>'1'</c>, and a quantity
///   is compared child by child with an element that has its children but no quantity type of its own. A
///   quantity reads its magnitude from a <c>value</c> String as well as a number, so an identifier whose value
///   is all digits is quantity-shaped too.
///   </description></item>
///   <item><description>
///   Temporals, and Strings whose text parses as a temporal, share <see cref="TemporalLikeTag"/>: two temporals
///   equal as instants may have different text, and a temporal equals a String with its text.
///   </description></item>
///   <item><description>
///   A String carrying a numeric instance type gets no key at all, so neither does any element containing it: it
///   equals a quantity it parses to and a String with its text, and those two cannot share a constant without
///   giving every numeric-looking code the same key.
///   </description></item>
/// </list>
/// <para>
/// Losing discrimination on those children costs nothing that matters: the elements a set operation dedupes in
/// practice - codings, components, identifiers - still differ in their plain-text children.
/// </para>
/// </remarks>
internal static class ElementEqualityKey
{
    /// <summary>
    /// Levels below which a subtree is hashed as a constant. Bounds hashing cost on deep elements; any constant is
    /// consistent with equality, so a cut-off loses discrimination, never correctness.
    /// </summary>
    private const int MaxHashDepth = 8;

    private const int TextTag = 0x1F3A_5C71;
    private const int NumberTag = 0x2B8E_43D9;
    private const int BooleanTag = 0x5D21_9A07;
    private const int ComplexTag = 0x6C4F_1E83;
    private const int DeepTag = 0x7A93_2B5F;
    private const int QuantityLikeTag = 0x0D6B_7E21;
    private const int TemporalLikeTag = 0x3C17_A4E9;

    /// <summary>
    /// Every child a quantity can have, in FHIR (<c>Quantity</c> and its profiles) and as a System quantity.
    /// </summary>
    private static readonly FrozenSet<string> QuantityChildNames =
        new[] { "id", "extension", "value", "comparator", "unit", "system", "code" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Steps charged per child hashed. Hashing sorts each element's children and materialises them, which measured at
    /// several times the cost of evaluating a node; a cheaper charge let one evaluation hash for over five seconds.
    /// </summary>
    private const int HashedChildSteps = 10;

    private static readonly FhirPrimitive[] TemporalKinds =
        [FhirPrimitive.Date, FhirPrimitive.DateTime, FhirPrimitive.Instant, FhirPrimitive.Time];

    /// <summary>
    /// Computes the key of <paramref name="element"/>, charging <paramref name="budget"/> a step for every child it
    /// hashes: one key can walk a whole subtree, and a collection can hold many copies of a large one.
    /// </summary>
    /// <returns><see langword="false"/> when the element has no key and must be compared against everything.</returns>
    public static bool TryGetKey(IElement element, EvaluationBudget budget, out int key)
    {
        ArgumentNullException.ThrowIfNull(budget);
        key = 0;

        if (ValueOrdering.IsQuantity(element) || TemporalOperand.IsTemporal(element.Value, element.InstanceType))
        {
            return false;
        }

        switch (element.Value)
        {
            case null:
                return TryHashChildren(element, depth: 0, budget, out key);
            case int or long or decimal:
                FunctionHelpers.TryConvertToDecimal(element.Value, out var number);
                key = NumberKey(number);
                return true;
            case bool boolean:
                key = HashCode.Combine(BooleanTag, boolean);
                return true;
            case string when ValueOrdering.TryToDecimal(element, out _):
                // A String with a numeric instance type reads as a quantity, so it equals one; and it equals a
                // String with its text. It cannot share a key with both.
                return false;
        }

        if (WireValue.AsWireString(element.Value) is { } text)
        {
            key = TextHash(text);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The key of an Integer, Long or Decimal with this value, for looking a number up by the value a quantity
    /// converts to.
    /// </summary>
    public static int NumberKey(decimal value) => HashCode.Combine(NumberTag, value);

    /// <summary>
    /// Whether every child of <paramref name="element"/> has a name a quantity's child can have - the shape a
    /// complex element needs to equal a quantity child by child.
    /// </summary>
    public static bool HasOnlyQuantityChildNames(IElement element, EvaluationBudget budget)
    {
        foreach (var child in element.Children())
        {
            budget.Step();
            if (!QuantityChildNames.Contains(child.Name))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The contribution of a child to its parent's key; see the remarks on this type for why it differs from the
    /// top-level key.
    /// </summary>
    private static bool TryGetContribution(IElement child, int depth, EvaluationBudget budget, out int contribution)
    {
        contribution = 0;

        if (child.Value is FhirQuantity || IsNumber(child.Value))
        {
            contribution = QuantityLikeTag;
            return true;
        }

        if (TemporalOperand.IsTemporal(child.Value, child.InstanceType)
            && TemporalOperand.AsTemporal(child.Value, child.InstanceType) is not null)
        {
            contribution = TemporalLikeTag;
            return true;
        }

        switch (child.Value)
        {
            case null:
                return TryGetComplexContribution(child, depth, budget, out contribution);
            case bool boolean:
                contribution = HashCode.Combine(BooleanTag, boolean);
                return true;
            case string when ValueOrdering.TryToDecimal(child, out _):
                // A String with a numeric instance type: see the remarks on this type.
                return false;
        }

        if (WireValue.AsWireString(child.Value) is { } text)
        {
            contribution = ParsesAsTemporal(text) ? TemporalLikeTag : TextHash(text);
            return true;
        }

        return false;
    }

    /// <summary>
    /// A complex child, of a quantity type or not: quantity-like when it could equal a quantity child by child - its
    /// names are a quantity's and its <c>value</c> could be the magnitude
    /// <c>QuantityEvaluator.ExtractQuantityFromChildren</c> reads - otherwise a hash of its children.
    /// </summary>
    /// <remarks>
    /// One test decides for a quantity and for its untyped twin, which is what keeps their contributions equal: a
    /// readable quantity always passes it, and an unreadable one is compared only child by child, exactly as its
    /// twin is. A readable quantity with a child no quantity has would escape that recognition, so it goes unkeyed;
    /// no real quantity has one.
    /// </remarks>
    private static bool TryGetComplexContribution(IElement element, int depth, EvaluationBudget budget, out int contribution)
    {
        contribution = 0;

        if (ValueOrdering.IsQuantity(element)
            && ValueOrdering.AsQuantity(element) is not null
            && !HasOnlyQuantityChildNames(element, budget))
        {
            return false;
        }

        if (!TryHashChildren(element, depth, budget, out var structural))
        {
            return false;
        }

        contribution = LooksLikeAQuantity(element, depth, budget) ? QuantityLikeTag : structural;
        return true;
    }

    private static bool LooksLikeAQuantity(IElement element, int depth, EvaluationBudget budget)
    {
        var hasQuantityLikeValue = false;
        foreach (var child in element.Children())
        {
            budget.Step();
            if (!QuantityChildNames.Contains(child.Name))
            {
                return false;
            }

            if (child.Name == "value" && CouldEqualAMagnitude(child, depth, budget))
            {
                hasQuantityLikeValue = true;
            }
        }

        return hasQuantityLikeValue;
    }

    /// <summary>
    /// Whether a <c>value</c> child could equal the child a quantity reads its magnitude from: a number or a String
    /// that parses as one, or something quantity-like, which a bare number equals.
    /// </summary>
    private static bool CouldEqualAMagnitude(IElement value, int depth, EvaluationBudget budget) => value.Value switch
    {
        FhirQuantity => true,
        string text => decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
        null => TryGetContribution(value, depth + 1, budget, out var contribution) && contribution == QuantityLikeTag,
        var other => IsNumber(other),
    };

    private static bool TryHashChildren(IElement element, int depth, EvaluationBudget budget, out int hash)
    {
        hash = 0;

        if (depth >= MaxHashDepth)
        {
            hash = DeepTag;
            return true;
        }

        var combined = new HashCode();
        combined.Add(ComplexTag);

        // Same order FunctionHelpers.AreElementsEqual compares children in: a stable ordinal sort by name.
        foreach (var child in element.Children().OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            budget.Charge(HashedChildSteps);
            if (!TryGetContribution(child, depth + 1, budget, out var childHash))
            {
                return false;
            }

            combined.Add(string.GetHashCode(child.Name, StringComparison.Ordinal));
            combined.Add(childHash);
        }

        hash = combined.ToHashCode();
        return true;
    }

    private static bool IsNumber(object? value) =>
        value is int or long or decimal or double or float or short or byte or sbyte or ushort or uint or ulong;

    private static bool ParsesAsTemporal(string text)
    {
        foreach (var kind in TemporalKinds)
        {
            if (FhirTemporal.TryParse(text, kind, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static int TextHash(string text) => HashCode.Combine(TextTag, string.GetHashCode(text, StringComparison.Ordinal));
}

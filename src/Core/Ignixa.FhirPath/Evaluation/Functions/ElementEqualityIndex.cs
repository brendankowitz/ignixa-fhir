// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// A set of elements under FHIRPath equality (<see cref="FunctionHelpers.AreElementsEqual(IElement?, IElement?)"/>),
/// answering membership without comparing a candidate against every member.
/// </summary>
/// <remarks>
/// <para>
/// Members live in one of three places. Elements with an <see cref="ElementEqualityKey"/> are bucketed by it.
/// Quantities are grouped by unit and then by value, because a quantity equals another after unit conversion and so
/// has no key of its own. Everything else - temporals, unreadable quantities - is kept in a list that every lookup
/// scans.
/// </para>
/// <para>
/// A lookup compares the candidate with every member the old linear scan could have found equal, and skips only
/// members a key or a value has already proved unequal, so membership answers exactly what the scan answered. That
/// turns <c>distinct()</c>, <c>|</c>, <c>intersect()</c>, <c>exclude()</c> and <c>repeat()</c> from quadratic to
/// linear on such data (#533), which is what search indexing over <c>Observation.code | Observation.component.code</c>
/// and <c>Observation.component.value as Quantity</c> needs.
/// </para>
/// <para>
/// The cross-kind cases are probed explicitly. A quantity is looked up in each unit group of its compatibility class
/// (<see cref="ValueOrdering.CompatibilityClass"/>) as the value <see cref="ValueOrdering.TryExpressIn"/> gives in
/// that group's unit, which is the value equality itself would
/// compare; where no conversion applies, equality falls through to comparing the quantities as they are, so only
/// the same unit and value can match. A number is also a quantity in unit <c>'1'</c>, so numbers and quantities probe
/// each other. And a quantity is compared child by child with a complex element of another type that has its
/// children, so quantities and quantity-shaped complex elements scan each other.
/// </para>
/// <para>
/// Comparisons are made as <c>AreElementsEqual(member, candidate)</c>, the argument order the scans used. Every key
/// and every comparison is charged to the evaluation budget, so what remains quadratic - unkeyed data, or members
/// that collide - is still bounded.
/// </para>
/// </remarks>
internal sealed class ElementEqualityIndex
{
    private readonly EvaluationBudget _budget;
    private readonly Action? _beforeComparison;
    private readonly Dictionary<int, List<IElement>> _keyed = [];
    private readonly List<IElement> _unkeyed = [];
    private readonly List<IElement> _quantityShaped = [];
    private readonly Dictionary<string, Dictionary<string, Dictionary<decimal, List<IElement>>>> _quantitiesByClass = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _classByUnit = new(StringComparer.Ordinal);
    private readonly List<IElement> _quantities = [];
    private int _numberCount;

    /// <summary>
    /// Steps charged for work that parses a UCUM unit: computing a unit's compatibility class, or converting a value
    /// between two different units. Each costs tens of microseconds rather than the fraction of one a step stands
    /// for, so a collection of quantities in many units drains the budget in proportion to what it costs.
    /// </summary>
    private const int UnitAlgebraSteps = 64;

    /// <param name="budget">The evaluation budget charged for each key and comparison.</param>
    /// <param name="beforeComparison">Called before each comparison, for a caller enforcing its own comparison limit.</param>
    public ElementEqualityIndex(EvaluationBudget budget, Action? beforeComparison = null)
    {
        ArgumentNullException.ThrowIfNull(budget);

        _budget = budget;
        _beforeComparison = beforeComparison;
    }

    /// <summary>
    /// Adds <paramref name="element"/> without checking whether an equal member is already present.
    /// </summary>
    public void Add(IElement element) => Add(Classify(element));

    /// <summary>
    /// Adds <paramref name="element"/> unless an equal member is already present.
    /// </summary>
    /// <returns><see langword="true"/> when the element was added.</returns>
    public bool AddIfAbsent(IElement element)
    {
        var entry = Classify(element);
        if (Contains(entry))
        {
            return false;
        }

        Add(entry);
        return true;
    }

    /// <summary>
    /// Reports whether a member equal to <paramref name="candidate"/> is present.
    /// </summary>
    public bool Contains(IElement candidate) => Contains(Classify(candidate));

    private Entry Classify(IElement element)
    {
        _budget.Step();

        if (ValueOrdering.IsQuantity(element))
        {
            // A complex quantity is also compared child by child with quantity-shaped elements, which are recognised
            // by their child names; one with any other child would escape that, so it is scanned instead.
            var groupable = element.Value is FhirQuantity || ElementEqualityKey.HasOnlyQuantityChildNames(element, _budget);
            return groupable && ValueOrdering.AsQuantity(element) is { } quantity
                ? new Entry(element, EntryKind.Quantity, Key: 0, quantity, Number: null)
                : new Entry(element, EntryKind.Unkeyed, Key: 0, Quantity: null, Number: null);
        }

        if (!ElementEqualityKey.TryGetKey(element, _budget, out var key))
        {
            return new Entry(element, EntryKind.Unkeyed, Key: 0, Quantity: null, Number: null);
        }

        var number = element.Value is int or long or decimal && FunctionHelpers.TryConvertToDecimal(element.Value, out var value)
            ? value
            : (decimal?)null;

        var kind = element.Value is null && ElementEqualityKey.HasOnlyQuantityChildNames(element, _budget)
            ? EntryKind.QuantityShaped
            : EntryKind.Keyed;

        return new Entry(element, kind, key, Quantity: null, number);
    }

    private void Add(Entry entry)
    {
        switch (entry.Kind)
        {
            case EntryKind.Unkeyed:
                _unkeyed.Add(entry.Element);
                return;

            case EntryKind.Quantity:
                AddQuantity(entry.Element, entry.Quantity!);
                return;

            case EntryKind.QuantityShaped:
                _quantityShaped.Add(entry.Element);
                AddKeyed(entry.Element, entry.Key);
                return;

            default:
                AddKeyed(entry.Element, entry.Key);
                if (entry.Number is not null)
                {
                    _numberCount++;
                }

                return;
        }
    }

    private void AddKeyed(IElement element, int key)
    {
        if (!_keyed.TryGetValue(key, out var bucket))
        {
            bucket = [];
            _keyed[key] = bucket;
        }

        bucket.Add(element);
    }

    private void AddQuantity(IElement element, FhirQuantity quantity)
    {
        var compatibilityClass = ClassOf(quantity.Unit);
        if (!_quantitiesByClass.TryGetValue(compatibilityClass, out var byUnit))
        {
            byUnit = new Dictionary<string, Dictionary<decimal, List<IElement>>>(StringComparer.Ordinal);
            _quantitiesByClass[compatibilityClass] = byUnit;
        }

        if (!byUnit.TryGetValue(quantity.Unit, out var byValue))
        {
            byValue = [];
            byUnit[quantity.Unit] = byValue;
        }

        if (!byValue.TryGetValue(quantity.Value, out var members))
        {
            members = [];
            byValue[quantity.Value] = members;
        }

        members.Add(element);
        _quantities.Add(element);
    }

    private bool Contains(Entry entry)
    {
        var candidate = entry.Element;

        switch (entry.Kind)
        {
            case EntryKind.Unkeyed:
                return AnyKeyedEqual(candidate) || AnyEqual(_quantities, candidate) || AnyEqual(_unkeyed, candidate);

            case EntryKind.Quantity:
                return AnyQuantityEqual(entry.Quantity!, candidate)
                    || (_numberCount > 0
                        && string.Equals(ClassOf(entry.Quantity!.Unit), ClassOf("1"), StringComparison.Ordinal)
                        && ValueOrdering.TryExpressIn(entry.Quantity!, "1", out var asNumber)
                        && BucketContains(ElementEqualityKey.NumberKey(asNumber), candidate))
                    || AnyEqual(_quantityShaped, candidate)
                    || AnyEqual(_unkeyed, candidate);

            default:
                return BucketContains(entry.Key, candidate)
                    || AnyEqual(_unkeyed, candidate)
                    || (entry.Number is { } number && AnyQuantityEqual(new FhirQuantity(number, "1"), candidate))
                    || (entry.Kind == EntryKind.QuantityShaped && AnyEqual(_quantities, candidate));
        }
    }

    /// <summary>
    /// Finds a quantity member equal to <paramref name="candidate"/>, read as <paramref name="quantity"/>: in each unit
    /// group of the quantity's compatibility class, the members holding the value equality would compare, or - where
    /// no conversion relates the units and equality falls through to the quantities as they are - the members with the
    /// same unit and value. Units of another class are incompatible, so equality could only fall through for them, and
    /// that needs the same unit.
    /// </summary>
    private bool AnyQuantityEqual(FhirQuantity quantity, IElement candidate)
    {
        // Checked first because computing a class parses the unit: a union of plain numbers must not pay for it.
        if (_quantitiesByClass.Count == 0)
        {
            return false;
        }

        if (!_quantitiesByClass.TryGetValue(ClassOf(quantity.Unit), out var byUnit))
        {
            return false;
        }

        foreach (var (unit, byValue) in byUnit)
        {
            _budget.Charge(string.Equals(unit, quantity.Unit, StringComparison.Ordinal) ? 1 : UnitAlgebraSteps);

            if (ValueOrdering.TryExpressIn(quantity, unit, out var converted))
            {
                if (byValue.TryGetValue(converted, out var aligned) && AnyEqual(aligned, candidate))
                {
                    return true;
                }
            }
            else if (string.Equals(unit, quantity.Unit, StringComparison.Ordinal)
                && byValue.TryGetValue(quantity.Value, out var same)
                && AnyEqual(same, candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The compatibility class of a unit, computed once per distinct unit this index sees.
    /// </summary>
    private string ClassOf(string unit)
    {
        if (!_classByUnit.TryGetValue(unit, out var compatibilityClass))
        {
            _budget.Charge(UnitAlgebraSteps);
            compatibilityClass = ValueOrdering.CompatibilityClass(unit);
            _classByUnit[unit] = compatibilityClass;
        }

        return compatibilityClass;
    }

    private bool BucketContains(int key, IElement candidate) =>
        _keyed.TryGetValue(key, out var bucket) && AnyEqual(bucket, candidate);

    private bool AnyKeyedEqual(IElement candidate)
    {
        foreach (var bucket in _keyed.Values)
        {
            if (AnyEqual(bucket, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private bool AnyEqual(List<IElement> members, IElement candidate)
    {
        foreach (var member in members)
        {
            _beforeComparison?.Invoke();
            _budget.Step();

            if (FunctionHelpers.AreElementsEqual(member, candidate, _budget))
            {
                return true;
            }
        }

        return false;
    }

    private enum EntryKind
    {
        Unkeyed,
        Keyed,
        QuantityShaped,
        Quantity,
    }

    /// <summary>An element with what its classification found, computed once per add or lookup.</summary>
    private readonly record struct Entry(IElement Element, EntryKind Kind, int Key, FhirQuantity? Quantity, decimal? Number);
}

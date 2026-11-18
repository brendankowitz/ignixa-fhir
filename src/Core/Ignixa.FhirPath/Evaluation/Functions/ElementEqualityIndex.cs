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
/// The cross-kind cases are probed explicitly. Quantities are grouped by compatibility class
/// (<see cref="ValueOrdering.CompatibilityClass"/>). Within a class, each unit that converts linearly into the class's
/// first unit is given a scale, once, so a quantity's magnitude in that unit is its value times the scale; quantities
/// equal after conversion have magnitudes within rounding of each other, so a lookup probes the magnitude's bucket and
/// its two neighbours and confirms with equality itself. A unit without a scale is probed the old way: the candidate
/// is converted into it with <see cref="ValueOrdering.TryExpressIn"/>, or, where no conversion applies and equality
/// falls through to comparing the quantities as they are, matched on the same unit and value. A number is also a
/// quantity in unit <c>'1'</c>, so numbers and quantities probe each other. And a quantity is compared child by child
/// with a complex element of another type that has its children, so quantities and quantity-shaped complex elements
/// scan each other.
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
    private readonly Dictionary<string, QuantityClass> _quantitiesByClass = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _classByUnit = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double?> _scaleByUnit = new(StringComparer.Ordinal);
    private readonly List<IElement> _quantities = [];
    private int _numberCount;

    // The unit costs below were measured warm in a Release build, where the step budget runs at about 3,650 steps
    // a millisecond (10,000,000 steps of nested select() took 2.74 s). Each charge is set at or above the worst
    // measured case, so a collection built to make the index do UCUM work exhausts the budget no slower than any
    // other expression.

    /// <summary>
    /// Steps charged for computing the compatibility class of a unit UCUM can read: one parse, measured at 0.50 ms
    /// (<c>m2</c>, <c>m3</c>, ...) to 0.65 ms (annotated spellings such as <c>g.{a1}</c>), about 1,800-2,400 steps.
    /// </summary>
    private const int UnitParseSteps = 3_000;

    /// <summary>
    /// Steps charged for computing the class of a unit UCUM cannot read, which the parser rejects early: measured at
    /// 0.012-0.11 ms (<c>widget1</c>, <c>[foo1]</c>, <c>m/zz1</c>), at most about 400 steps.
    /// </summary>
    private const int UnreadableUnitSteps = 1_000;

    /// <summary>
    /// Steps charged for converting a value between two different units, which parses both: measured at 1.2 ms
    /// (<c>mg</c> to <c>kg</c>), 2.2 ms (<c>[lb_av]</c> to <c>g</c>, or between annotated spellings) and 0.5 ms
    /// for a conversion that fails - about 4,400-8,000 steps. Conversions are made once per unit to give it a
    /// scale, for each comparison of quantities in different units, and per unit without a scale on every lookup.
    /// </summary>
    private const int UnitConversionSteps = 12_000;

    /// <summary>
    /// The relative width of a magnitude bucket. Magnitudes of equal quantities differ only by decimal rounding
    /// (about 1e-27 relative), so they always fall in the same or adjacent buckets.
    /// </summary>
    private const double BucketWidth = 1e-9;

    /// <summary>
    /// Values (and scales) below this keep fewer than 15 significant digits within decimal's 28 places, so they are
    /// compared directly rather than by magnitude.
    /// </summary>
    private const decimal PreciseMagnitude = 1e-13m;

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
        if (!_quantitiesByClass.TryGetValue(compatibilityClass, out var members))
        {
            members = new QuantityClass(quantity.Unit);
            _quantitiesByClass[compatibilityClass] = members;
        }

        var isNewUnit = !members.ByUnit.TryGetValue(quantity.Unit, out var byValue);
        if (isNewUnit)
        {
            byValue = [];
            members.ByUnit[quantity.Unit] = byValue;
        }

        if (!byValue!.TryGetValue(quantity.Value, out var sameValue))
        {
            sameValue = [];
            byValue[quantity.Value] = sameValue;
        }

        sameValue.Add(element);
        _quantities.Add(element);
        members.All.Add((element, quantity.Unit));

        var scale = ScaleOf(quantity.Unit, members);
        if (scale is null && isNewUnit)
        {
            members.UnscaledUnits.Add(quantity.Unit);
        }

        if (IsNearTheScaleLimit(quantity.Value))
        {
            members.Imprecise.Add((element, quantity.Unit));
        }
        else if (scale is not null)
        {
            var bucket = BucketOf(quantity.Value, scale.Value);
            if (!members.ByMagnitude.TryGetValue(bucket, out var near))
            {
                near = [];
                members.ByMagnitude[bucket] = near;
            }

            near.Add((element, quantity.Unit));
        }
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

        if (!_quantitiesByClass.TryGetValue(ClassOf(quantity.Unit), out var members))
        {
            return false;
        }

        if (IsNearTheScaleLimit(quantity.Value))
        {
            // A conversion that lands near decimal's 28-place limit keeps few significant digits, so an equal member
            // can sit any distance away by magnitude or converted value: compare with every member of the class.
            return AnyEqualIn(members.All, quantity.Unit, candidate);
        }

        if (AnyEqualIn(members.Imprecise, quantity.Unit, candidate))
        {
            return true;
        }

        if (ScaleOf(quantity.Unit, members) is not { } scale)
        {
            // Without a scale the candidate cannot be placed among the scaled members, so it probes every unit.
            return AnyEqualInUnits(quantity, candidate, members, members.ByUnit.Keys);
        }

        var bucket = BucketOf(quantity.Value, scale);
        for (var neighbour = bucket.Magnitude - 1; neighbour <= bucket.Magnitude + 1; neighbour++)
        {
            if (members.ByMagnitude.TryGetValue((bucket.Sign, neighbour), out var near) && AnyEqualIn(near, quantity.Unit, candidate))
            {
                return true;
            }
        }

        return AnyEqualInUnits(quantity, candidate, members, members.UnscaledUnits);
    }

    /// <summary>
    /// Whether a value is small enough that decimal's 28-place scale leaves it, or a conversion that yields it, with
    /// fewer significant digits than the magnitude buckets assume. Equal quantities convert into each other's
    /// values, so a pair can only be mis-bucketed when one of their values is this small.
    /// </summary>
    private static bool IsNearTheScaleLimit(decimal value) => value != 0m && Math.Abs(value) < PreciseMagnitude;

    /// <summary>
    /// In each of <paramref name="units"/>, finds a member holding the value equality would compare with
    /// <paramref name="quantity"/> converted into that unit, or - where no conversion applies - the same unit and value.
    /// </summary>
    private bool AnyEqualInUnits(FhirQuantity quantity, IElement candidate, QuantityClass members, IEnumerable<string> units)
    {
        foreach (var unit in units)
        {
            var byValue = members.ByUnit[unit];
            _budget.Charge(string.Equals(unit, quantity.Unit, StringComparison.Ordinal) ? 1 : UnitConversionSteps);

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
    /// Compares the candidate with members of nearly the same magnitude, charging a conversion for each member in
    /// another unit, since equality converts between them.
    /// </summary>
    private bool AnyEqualIn(List<(IElement Element, string Unit)> near, string candidateUnit, IElement candidate)
    {
        foreach (var (member, unit) in near)
        {
            _beforeComparison?.Invoke();
            _budget.Charge(string.Equals(unit, candidateUnit, StringComparison.Ordinal) ? 1 : UnitConversionSteps);

            if (FunctionHelpers.AreElementsEqual(member, candidate, _budget))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The factor that takes a value in <paramref name="unit"/> to the class's first unit, computed once per unit;
    /// null when the unit does not convert into it linearly (an annotated or special unit such as <c>Cel</c>), which
    /// leaves the unit to be probed by conversion.
    /// </summary>
    private double? ScaleOf(string unit, QuantityClass members)
    {
        if (_scaleByUnit.TryGetValue(unit, out var known))
        {
            return known;
        }

        double? scale = null;
        if (string.Equals(unit, members.Representative, StringComparison.Ordinal))
        {
            scale = 1d;
        }
        else
        {
            _budget.Charge(UnitConversionSteps);
            if (ValueOrdering.TryExpressIn(new FhirQuantity(1m, unit), members.Representative, out var one)
                && one != 0m
                && !IsNearTheScaleLimit(one))
            {
                // A second point rules out an offset (Cel to K, should a converter support it): only a linear
                // conversion makes magnitudes comparable.
                _budget.Charge(UnitConversionSteps);
                if (ValueOrdering.TryExpressIn(new FhirQuantity(2m, unit), members.Representative, out var two) && two == 2m * one)
                {
                    scale = (double)one;
                }
            }
        }

        _scaleByUnit[unit] = scale;
        return scale;
    }

    private static (int Sign, long Magnitude) BucketOf(decimal value, double scale)
    {
        var magnitude = (double)value * scale;
        return magnitude == 0d
            ? (0, 0)
            : (Math.Sign(magnitude), (long)Math.Floor(Math.Log(Math.Abs(magnitude)) / BucketWidth));
    }

    /// <summary>
    /// The compatibility class of a unit, computed once per distinct unit this index sees.
    /// </summary>
    private string ClassOf(string unit)
    {
        if (!_classByUnit.TryGetValue(unit, out var compatibilityClass))
        {
            compatibilityClass = ValueOrdering.CompatibilityClass(unit);
            _budget.Charge(compatibilityClass.StartsWith("unit:", StringComparison.Ordinal) ? UnreadableUnitSteps : UnitParseSteps);
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

    /// <summary>The quantity members of one compatibility class.</summary>
    /// <param name="representative">The first unit seen in the class, which scales are relative to.</param>
    private sealed class QuantityClass(string representative)
    {
        public string Representative { get; } = representative;

        /// <summary>Every member, by unit and then by value.</summary>
        public Dictionary<string, Dictionary<decimal, List<IElement>>> ByUnit { get; } = new(StringComparer.Ordinal);

        /// <summary>Members in units with a scale, by the bucket of their magnitude in the representative unit.</summary>
        public Dictionary<(int Sign, long Magnitude), List<(IElement Element, string Unit)>> ByMagnitude { get; } = [];

        /// <summary>Units of members that have no scale, in the order first seen.</summary>
        public List<string> UnscaledUnits { get; } = [];

        /// <summary>Members whose value is near decimal's scale limit, compared with every lookup.</summary>
        public List<(IElement Element, string Unit)> Imprecise { get; } = [];

        /// <summary>Every member, for a lookup whose own value is near decimal's scale limit.</summary>
        public List<(IElement Element, string Unit)> All { get; } = [];
    }
}

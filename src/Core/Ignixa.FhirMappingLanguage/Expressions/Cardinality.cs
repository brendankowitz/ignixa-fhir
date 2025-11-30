/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Represents cardinality constraints for source elements.
/// Example: 0..1, 1..*, 0..*
/// </summary>
public class Cardinality
{
    public Cardinality(int min, int? max)
    {
        if (min < 0)
        {
            throw new ArgumentException("Minimum cardinality cannot be negative", nameof(min));
        }

        if (max.HasValue && max.Value < min)
        {
            throw new ArgumentException("Maximum cardinality cannot be less than minimum", nameof(max));
        }

        Min = min;
        Max = max;
    }

    /// <summary>
    /// Minimum number of elements (inclusive).
    /// </summary>
    public int Min { get; }

    /// <summary>
    /// Maximum number of elements (inclusive). Null means unbounded (*).
    /// </summary>
    public int? Max { get; }

    /// <summary>
    /// Returns true if the given count satisfies this cardinality constraint.
    /// </summary>
    public bool IsSatisfiedBy(int count)
    {
        if (count < Min)
        {
            return false;
        }

        if (Max.HasValue && count > Max.Value)
        {
            return false;
        }

        return true;
    }

    public override string ToString() => Max.HasValue ? $"{Min}..{Max}" : $"{Min}..*";
}

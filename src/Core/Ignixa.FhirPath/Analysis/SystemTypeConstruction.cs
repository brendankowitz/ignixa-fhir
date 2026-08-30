// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;

namespace Ignixa.FhirPath.Analysis;

internal sealed class SystemTypeConstruction
{
    private readonly FrozenSet<string> _typeNames;

    private SystemTypeConstruction(
        bool mayConstructAny,
        bool isKnownEmpty,
        bool mayYieldFhirValue,
        IEnumerable<string> typeNames)
    {
        MayConstructAny = mayConstructAny;
        IsKnownEmpty = isKnownEmpty;
        MayYieldFhirValue = mayYieldFhirValue;
        _typeNames = typeNames.ToFrozenSet(StringComparer.Ordinal);
    }

    public static SystemTypeConstruction Any { get; } = new(true, false, true, []);

    public static SystemTypeConstruction Empty { get; } = new(false, true, false, []);

    public static SystemTypeConstruction None { get; } = new(false, false, true, []);

    public static SystemTypeConstruction Numeric { get; } =
        new(false, false, false, ["integer", "decimal", "Quantity"]);

    public bool MayConstructAny { get; }

    public bool IsKnownEmpty { get; }

    private bool MayYieldFhirValue { get; }

    public IReadOnlySet<string> TypeNames =>
        MayConstructAny
            ? throw new InvalidOperationException("Unknown System-type construction cannot be enumerated.")
            : _typeNames;

    /// <summary>
    /// Returns the provenance of a value constructed with a single, known System type.
    /// </summary>
    public static SystemTypeConstruction For(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        return new SystemTypeConstruction(false, false, false, [typeName]);
    }

    public SystemTypeConstruction Union(SystemTypeConstruction other) =>
        MayConstructAny || other.MayConstructAny
            ? Any
            : new SystemTypeConstruction(
                false,
                IsKnownEmpty && other.IsKnownEmpty,
                MayYieldFhirValue || other.MayYieldFhirValue,
                TypeNames.Concat(other.TypeNames));

    /// <summary>
    /// Returns the System types that unary minus can construct from this provenance.
    /// </summary>
    /// <remarks>
    /// For a finite FHIR-valued operand the evaluator admits only integer, long, decimal, double, float
    /// and Quantity values, whose negation creates only integer, decimal or Quantity results, so
    /// <see cref="Numeric"/> is exhaustive for that state. The switch below is exhaustive over a narrower
    /// set — the type names this analysis produces, which folds double and float into <c>decimal</c> — so
    /// an unrecognised name means the analysis has grown a name the negation rule has not been taught,
    /// and the answer is unknown rather than nothing.
    /// </remarks>
    public SystemTypeConstruction Negate()
    {
        if (IsKnownEmpty)
        {
            return Empty;
        }

        if (MayConstructAny)
        {
            return Any;
        }

        if (MayYieldFhirValue)
        {
            return Numeric;
        }

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var typeName in TypeNames)
        {
            switch (typeName)
            {
                case "integer":
                    result.Add("integer");
                    break;
                case "long":
                    result.Add("integer");
                    result.Add("decimal");
                    break;
                case "decimal":
                    result.Add("decimal");
                    break;
                case "Quantity":
                    result.Add("Quantity");
                    break;
                default:
                    return Any;
            }
        }

        return new SystemTypeConstruction(false, false, false, result);
    }
}

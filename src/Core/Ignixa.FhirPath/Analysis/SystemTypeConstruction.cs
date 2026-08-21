// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirPath.Analysis;

internal sealed class SystemTypeConstruction
{
    private readonly IReadOnlySet<string> _typeNames;

    private SystemTypeConstruction(
        bool mayConstructAny,
        bool isKnownEmpty,
        bool mayYieldFhirValue,
        IEnumerable<string> typeNames)
    {
        MayConstructAny = mayConstructAny;
        IsKnownEmpty = isKnownEmpty;
        MayYieldFhirValue = mayYieldFhirValue;
        _typeNames = typeNames.ToHashSet(StringComparer.Ordinal);
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

    public static SystemTypeConstruction For(string typeName) => new(false, false, false, [typeName]);

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
    /// For a finite FHIR-valued operand, the evaluator admits only integer, long, decimal, double,
    /// float, and Quantity values. Its numeric negation creates only integer, decimal, or Quantity
    /// results, so <see cref="Numeric"/> is exhaustive for that state. Unknown construction provenance
    /// remains <see cref="Any"/> because it cannot be reduced to that finite input contract.
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

        var result = TypeNames.SelectMany(typeName => typeName switch
        {
            "integer" => ["integer"],
            "long" => ["integer", "decimal"],
            "decimal" => ["decimal"],
            "Quantity" => ["Quantity"],
            _ => Array.Empty<string>(),
        });
        return new SystemTypeConstruction(false, false, false, result);
    }
}

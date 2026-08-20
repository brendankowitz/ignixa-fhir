namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

public enum ParityResourceFeature
{
    ChoiceQuantity,
    ChoiceDateTime,
    ChoiceString,
    ChoiceRatio,
    CardinalityZero,
    CardinalityOne,
    CardinalityMany,
    PartialPrecisionTemporal,
    EquivalentOffsetTemporal,
    CompatibleUnits,
    IncompatibleUnits,
    CalendarQuantity,
    ResolvePresent,
    ResolveAbsent,
    ResolveContained,
    QuantityEquivalence,
}

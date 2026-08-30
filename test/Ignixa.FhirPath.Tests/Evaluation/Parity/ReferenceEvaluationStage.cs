namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// Which step of the Firely reference index projection raised a failure.
/// </summary>
/// <remarks>
/// The stage is part of a failure's pinned signature because the same search parameter can fail at
/// more than one step, and the steps mean different things: a <see cref="Select"/> failure is an
/// evaluator divergence, whereas a <see cref="Convert"/> failure is the shared production converter
/// rejecting what Firely selected.
/// </remarks>
internal enum ReferenceEvaluationStage
{
    /// <summary>Evaluating a non-composite search parameter expression.</summary>
    Select,

    /// <summary>Converting a non-composite selected element into search values.</summary>
    Convert,

    /// <summary>Evaluating a composite search parameter's root expression.</summary>
    CompositeSelect,

    /// <summary>Evaluating one component expression of a composite search parameter.</summary>
    ComponentSelect,

    /// <summary>Converting one component's selected element into search values.</summary>
    ComponentConvert,
}

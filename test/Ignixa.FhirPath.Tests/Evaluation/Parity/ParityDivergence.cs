/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * One expression on which the two engines disagree.
 */

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// A single observed disagreement between Firely 5.11.4 and Ignixa.
/// </summary>
internal sealed record ParityDivergence(
    string Expression,
    string ResourceName,
    string Source,
    ParityOutcome Firely,
    ParityOutcome Ignixa)
{
    /// <summary>
    /// The expression plus the value-free shape of the disagreement.
    /// </summary>
    /// <remarks>
    /// Grouping on shape alone collapses unrelated root causes into one bucket - a quantity precision
    /// difference and a boundary-function bug both read as "one result versus one result" - so a new
    /// divergence could hide inside an existing entry by only moving its count. Naming the expression
    /// keeps each pinned row traceable to exactly one behaviour, and the count then means "how many
    /// subject resources reach it", which is the reachability signal the inventory is ranked on.
    /// </remarks>
    public string Signature => $"{Expression} :: firely={Firely.Shape()} ignixa={Ignixa.Shape()}";
}

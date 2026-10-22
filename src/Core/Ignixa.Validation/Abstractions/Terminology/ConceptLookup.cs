// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions.Terminology;

public sealed record ConceptLookup : ITerminologyAnswer
{
    private ConceptLookup(Determination result, ConceptDetails? concept, IReadOnlyList<UnknownReason> unknowns)
    {
        Result = result;
        Concept = concept;
        Unknowns = unknowns;
    }

    public static ConceptLookup NotFound { get; } = new(Determination.False, null, UnknownReasons.None);

    public Determination Result { get; }

    public ConceptDetails? Concept { get; }

    /// <summary>
    /// Every reason the lookup could not be determined, in source order; empty unless <see cref="Result"/> is
    /// <see cref="Determination.Unknown"/>.
    /// </summary>
    public IReadOnlyList<UnknownReason> Unknowns { get; }

    /// <summary>The first of <see cref="Unknowns"/>, or <see langword="null"/> when the lookup is determined.</summary>
    public UnknownReason? Unknown => Unknowns.Count > 0 ? Unknowns[0] : null;

    public static ConceptLookup Found(ConceptDetails concept)
    {
        ArgumentNullException.ThrowIfNull(concept);
        return new(Determination.True, concept, UnknownReasons.None);
    }

    public static ConceptLookup Undetermined(UnknownReason reason) =>
        new(Determination.Unknown, null, UnknownReasons.Single(reason));

    public static ConceptLookup Undetermined(IReadOnlyList<UnknownReason> reasons) =>
        new(Determination.Unknown, null, UnknownReasons.Copy(reasons));

    public bool Equals(ConceptLookup? other) =>
        other is not null
        && Result == other.Result
        && EqualityComparer<ConceptDetails?>.Default.Equals(Concept, other.Concept)
        && Unknowns.SequenceEqual(other.Unknowns);

    public override int GetHashCode() => HashCode.Combine(Result, Concept, UnknownReasons.Hash(Unknowns));
}

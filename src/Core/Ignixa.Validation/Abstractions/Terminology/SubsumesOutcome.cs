// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions.Terminology;

public abstract record SubsumesOutcome : ITerminologyAnswer
{
    private SubsumesOutcome()
    {
    }

    public SubsumptionRelation? KnownRelation => this is Related related ? related.Relation : null;

    public UnknownReason? Unknown => this is Undetermined undetermined ? undetermined.Reason : null;

    public IReadOnlyList<UnknownReason> Unknowns => this is Undetermined undetermined ? undetermined.Reasons : UnknownReasons.None;

    public sealed record Related(SubsumptionRelation Relation) : SubsumesOutcome
    {
        public SubsumptionRelation Relation { get; } = EnsureRelation(Relation);

        private static SubsumptionRelation EnsureRelation(SubsumptionRelation relation) =>
            Enum.IsDefined(relation)
                ? relation
                : throw new ArgumentOutOfRangeException(nameof(relation), relation, null);
    }

    public sealed record Undetermined : SubsumesOutcome
    {
        public Undetermined(UnknownReason reason)
        {
            Reasons = UnknownReasons.Single(reason);
        }

        public Undetermined(IReadOnlyList<UnknownReason> reasons)
        {
            Reasons = UnknownReasons.Copy(reasons);
        }

        /// <summary>Every reason subsumption could not be determined, in source order; never empty.</summary>
        public IReadOnlyList<UnknownReason> Reasons { get; }

        /// <summary>The first of <see cref="Reasons"/>.</summary>
        public UnknownReason Reason => Reasons[0];

        public bool Equals(Undetermined? other) => other is not null && Reasons.SequenceEqual(other.Reasons);

        public override int GetHashCode() => UnknownReasons.Hash(Reasons);
    }
}

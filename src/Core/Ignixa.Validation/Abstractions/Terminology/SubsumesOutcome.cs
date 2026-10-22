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

    public sealed record Related(SubsumptionRelation Relation) : SubsumesOutcome
    {
        public SubsumptionRelation Relation { get; } = EnsureRelation(Relation);

        private static SubsumptionRelation EnsureRelation(SubsumptionRelation relation) =>
            Enum.IsDefined(relation)
                ? relation
                : throw new ArgumentOutOfRangeException(nameof(relation), relation, null);
    }

    public sealed record Undetermined(UnknownReason Reason) : SubsumesOutcome
    {
        public UnknownReason Reason { get; } = EnsureReason(Reason);

        private static UnknownReason EnsureReason(UnknownReason reason)
        {
            ArgumentNullException.ThrowIfNull(reason);
            return reason;
        }
    }
}

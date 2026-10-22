// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions.Terminology;

public static class SubsumptionRelationCodes
{
    public static string ToFhirCode(this SubsumptionRelation relation) => relation switch
    {
        SubsumptionRelation.Equivalent => "equivalent",
        SubsumptionRelation.Subsumes => "subsumes",
        SubsumptionRelation.SubsumedBy => "subsumed-by",
        SubsumptionRelation.NotSubsumed => "not-subsumed",
        _ => throw new ArgumentOutOfRangeException(nameof(relation), relation, null),
    };
}

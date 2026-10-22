// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions.Terminology;

public sealed record ConceptLookup : ITerminologyAnswer
{
    private ConceptLookup(Determination result, ConceptDetails? concept, UnknownReason? unknown)
    {
        Result = result;
        Concept = concept;
        Unknown = unknown;
    }

    public static ConceptLookup NotFound { get; } = new(Determination.False, null, null);

    public Determination Result { get; }

    public ConceptDetails? Concept { get; }

    public UnknownReason? Unknown { get; }

    public static ConceptLookup Found(ConceptDetails concept)
    {
        ArgumentNullException.ThrowIfNull(concept);
        return new(Determination.True, concept, null);
    }

    public static ConceptLookup Undetermined(UnknownReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return new(Determination.Unknown, null, reason);
    }
}

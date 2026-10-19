/*
 * Copyright (c) 2026, Ignixa Contributors
 */

using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

[AttributeUsage(AttributeTargets.Method)]
public sealed class FmlIncubatorTheoryAttribute : TheoryAttribute
{
    public const string CorpusPathVariable = "FML_INCUBATOR_PATH";

    public FmlIncubatorTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CorpusPathVariable)))
        {
            Skip = $"Set {CorpusPathVariable} to a local HL7/mapping-language-ig checkout to run these conformance tests.";
        }
    }
}

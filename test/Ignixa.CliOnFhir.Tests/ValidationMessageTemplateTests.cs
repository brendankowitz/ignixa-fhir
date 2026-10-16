// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Validation;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// The message templates validation summaries show (Task 9): validator messages interpolate data, quoted and unquoted.
/// New behavior: acdd94f has no validation summary (verified: this file does not compile there).
/// </summary>
public class ValidationMessageTemplateTests
{
    private static readonly HashSet<string> TypeNames = new(["Patient"], StringComparer.Ordinal);

    [Fact]
    public void GivenAMessageQuotingSchemaTokensAndDataValues_WhenTemplated_ThenOnlyTheDataValuesAreReplaced()
    {
        IReadOnlyList<string> vocabulary = MessageTemplate.Vocabulary(JsonNode.Parse("""{"resourceType":"Patient","birthDate":"abc","gender":"x"}""")!, TypeNames);

        MessageTemplate.Of("type-1: Value 'abc' is not a valid 'date' for Patient ('x')", vocabulary).ShouldBe("type-1: Value '…' is not a valid 'date' for Patient ('…')");
    }

    [Fact]
    public void GivenADataValueInterpolatedWithoutQuotes_WhenTemplated_ThenEachWholeTokenOccurrenceIsReplaced()
    {
        IReadOnlyList<string> vocabulary = MessageTemplate.Vocabulary(JsonNode.Parse("""{"resourceType":"Patient","url":"http://evil.example/run now","code":"final"}""")!, TypeNames);

        MessageTemplate.Of("Example URLs are not allowed in this context (http://evil.example/run now); finalize final", vocabulary)
            .ShouldBe("Example URLs are not allowed in this context (…); finalize …");
    }

    [Fact]
    public void GivenATemplateLongerThanTheLimit_WhenTemplated_ThenItIsDropped()
    {
        MessageTemplate.Of(new string('a', MessageTemplate.MaxLength + 1), []).ShouldBeNull();
    }
}

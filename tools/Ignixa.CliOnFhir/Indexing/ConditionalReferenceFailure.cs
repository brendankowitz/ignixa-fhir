// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Indexing;

/// <summary>
/// Why a conditional reference (<c>Type?criteria</c>) was left as written: a closed set, each with the reason code issues name
/// it by and what that code means. Persisted in the index cache by its code; an unknown code fails the read, so the cache
/// file is rebuilt.
/// </summary>
[JsonConverter(typeof(Converter))]
public sealed class ConditionalReferenceFailure
{
    /// <summary>No resource of the type in the folder has the identifier (a server rejects the transaction).</summary>
    public static readonly ConditionalReferenceFailure NoMatch = new("conditional-no-match", "no resource of that type in the folder has the identifier");

    /// <summary>More than one resource of the type has it, and no <c>ifNoneExist</c> rules out the later ones (a server rejects the transaction).</summary>
    public static readonly ConditionalReferenceFailure Ambiguous = new("conditional-ambiguous", "more than one has it");

    /// <summary>The criteria are not one <c>identifier=[system|]value</c>, the only form the tool evaluates.</summary>
    public static readonly ConditionalReferenceFailure UnsupportedParameter = new("conditional-unsupported-parameter", "the criteria are not one 'identifier=[system|]value'");

    /// <summary>A resource of the type could not be read, so whether exactly one has the identifier is unknown.</summary>
    public static readonly ConditionalReferenceFailure TargetUnreadable = new("conditional-target-unreadable", "a resource of that type could not be read, so whether exactly one has the identifier is unknown");

    private ConditionalReferenceFailure(string code, string explanation)
    {
        Code = code;
        Explanation = explanation;
    }

    /// <summary>Every failure, in the order issues list them.</summary>
    public static IReadOnlyList<ConditionalReferenceFailure> All { get; } = [NoMatch, Ambiguous, UnsupportedParameter, TargetUnreadable];

    /// <summary>The reason code issues name it by.</summary>
    public string Code { get; }

    /// <summary>What the code means, for an issue message.</summary>
    public string Explanation { get; }

    public override string ToString() => Code;

    private sealed class Converter : JsonConverter<ConditionalReferenceFailure>
    {
        public override ConditionalReferenceFailure Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? code = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            return All.FirstOrDefault(failure => failure.Code == code)
                ?? throw new JsonException($"'{code}' is not a conditional reference failure code.");
        }

        public override void Write(Utf8JsonWriter writer, ConditionalReferenceFailure value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Code);
    }
}

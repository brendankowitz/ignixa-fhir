// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ignixa.CliOnFhir.Clock;

namespace Ignixa.CliOnFhir.Contracts;

/// <summary>
/// An instant written as a FHIR instant in UTC (<c>2020-01-01T00:00:00Z</c>), as the envelope's <c>asOf</c> writes it, rather than
/// as System.Text.Json writes a <see cref="DateTimeOffset"/> (<c>2020-01-01T00:00:00+00:00</c>).
/// </summary>
public sealed class UtcInstantConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(FhirInstant.Format(value.ToUniversalTime()));
    }
}

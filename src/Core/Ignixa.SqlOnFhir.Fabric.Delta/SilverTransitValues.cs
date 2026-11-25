// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// Reads the projector's transit encoding of temporal and decimal cells. The projector guarantees these forms, so a
/// cell that does not match means the projection and the contract disagree; failures name the column path and the
/// expected form, never the cell value.
/// </summary>
internal static class SilverTransitValues
{
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";
    private const string DateFormat = "yyyy-MM-dd";

    private static readonly DateOnly UnixEpochDate = DateOnly.FromDateTime(DateTime.UnixEpoch);

    /// <summary>Microseconds since the Unix epoch, UTC, of a <c>yyyy-MM-ddTHH:mm:ss.ffffffZ</c> string cell.</summary>
    public static long TimestampMicroseconds(JsonElement cell, string path)
    {
        if (cell.ValueKind != JsonValueKind.String ||
            !DateTime.TryParseExact(
                cell.GetString(),
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var instant))
        {
            throw Malformed(path, "a UTC timestamp at microsecond precision");
        }

        return (instant.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMicrosecond;
    }

    /// <summary>The proleptic Gregorian calendar date of a <c>yyyy-MM-dd</c> string cell.</summary>
    public static DateOnly Date(JsonElement cell, string path)
    {
        if (cell.ValueKind != JsonValueKind.String ||
            !DateOnly.TryParseExact(cell.GetString(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw Malformed(path, "a calendar date");
        }

        return date;
    }

    /// <summary>Days since the Unix epoch, negative before it.</summary>
    public static int DaysSinceEpoch(DateOnly date) => date.DayNumber - UnixEpochDate.DayNumber;

    /// <summary>
    /// The unscaled value of a decimal cell: a JSON number in plain notation with exactly <paramref name="scale"/>
    /// fraction digits and at most <paramref name="precision"/> digits in all. Read from the raw number text, because a
    /// precision-38 value does not fit <see cref="decimal"/>.
    /// </summary>
    public static BigInteger DecimalUnscaled(JsonElement cell, int precision, int scale, string path)
    {
        if (cell.ValueKind != JsonValueKind.Number)
        {
            throw Malformed(path, $"a decimal({precision},{scale}) number");
        }

        var text = cell.GetRawText().AsSpan();
        var negative = text.StartsWith("-");
        var unsigned = negative ? text[1..] : text;
        var point = unsigned.IndexOf('.');
        var integerDigits = point < 0 ? unsigned : unsigned[..point];
        var fractionDigits = point < 0 ? [] : unsigned[(point + 1)..];

        if (integerDigits.IsEmpty ||
            !IsDigits(integerDigits) ||
            !IsDigits(fractionDigits) ||
            (point >= 0 && fractionDigits.IsEmpty) ||
            fractionDigits.Length != scale ||
            SignificantIntegerDigits(integerDigits) + scale > precision)
        {
            throw Malformed(path, $"a decimal({precision},{scale}) number in plain notation with exactly {scale} fraction digits");
        }

        var unscaled = BigInteger.Parse(string.Concat(integerDigits, fractionDigits), NumberStyles.None, CultureInfo.InvariantCulture);
        return negative ? -unscaled : unscaled;
    }

    private static bool IsDigits(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    // Leading zeros (as in "0.5") take no precision.
    private static int SignificantIntegerDigits(ReadOnlySpan<char> integerDigits) =>
        integerDigits.TrimStart('0').Length;

    private static InvalidDataException Malformed(string path, string expected) =>
        new($"The projected cell at '{path}' is not {expected}; the projection does not match the Silver contract.");
}

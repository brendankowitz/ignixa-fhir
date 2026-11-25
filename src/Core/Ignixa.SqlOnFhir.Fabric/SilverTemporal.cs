// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// Reads FHIR temporal text into Silver <c>timestamp</c> and <c>date</c> values and writes them back as text.
/// </summary>
/// <remarks>
/// <para>
/// A timestamp accepts the FHIR <c>date</c>, <c>dateTime</c> and <c>instant</c> forms. A partial value (year or
/// year-month or a date without a time) becomes its first instant in UTC, and a time without an offset is read as UTC;
/// both are deterministic policy, not verified Spark behaviour. Silver stores microseconds, so non-zero digits beyond
/// the sixth fraction digit are rejected rather than silently truncated.
/// </para>
/// <para>A date accepts only the date forms (<c>YYYY</c>, <c>YYYY-MM</c>, <c>YYYY-MM-DD</c>); a partial date becomes its
/// first day.</para>
/// </remarks>
internal static class SilverTemporal
{
    private const int MaxOffsetHours = 14;
    private const int TicksPerMicrosecond = 10;
    private const int FractionDigitsKept = 6;

    public static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % TicksPerMicrosecond), TimeSpan.Zero);
    }

    /// <summary>The row encoding: UTC with six fraction digits.</summary>
    public static string FormatTimestamp(DateTimeOffset utc) =>
        utc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    /// <summary>The companion encoding: UTC truncated to milliseconds (the JSON writer's default pattern, UTC session).</summary>
    public static string FormatCompanionTimestamp(DateTimeOffset utc) =>
        utc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateTimeOffset ParseRowTimestamp(string text) =>
        DateTimeOffset.ParseExact(text, "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    public static bool TryParseTimestamp(string text, out DateTimeOffset value)
    {
        value = default;
        var reader = new Cursor(text);
        if (!TryReadDate(ref reader, out var date))
        {
            return false;
        }

        if (reader.AtEnd)
        {
            value = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            return true;
        }

        if (!reader.Accept('T') || !TryReadTime(ref reader, out var ticksOfDay) || !TryReadOffset(ref reader, out var offset))
        {
            return false;
        }

        if (!reader.AtEnd)
        {
            return false;
        }

        var local = date.ToDateTime(TimeOnly.MinValue).AddTicks(ticksOfDay);
        var utcTicks = local.Ticks - offset.Ticks;
        if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        value = new DateTimeOffset(utcTicks, TimeSpan.Zero);
        return true;
    }

    public static bool TryParseDate(string text, out DateOnly value)
    {
        var reader = new Cursor(text);
        return TryReadDate(ref reader, out value) && reader.AtEnd;
    }

    private static bool TryReadDate(ref Cursor reader, out DateOnly date)
    {
        date = default;
        if (!reader.TryReadDigits(4, out var year) || year < 1)
        {
            return false;
        }

        var month = 1;
        var day = 1;
        if (reader.Accept('-'))
        {
            if (!reader.TryReadDigits(2, out month) || month is < 1 or > 12)
            {
                return false;
            }

            if (reader.Accept('-') &&
                (!reader.TryReadDigits(2, out day) || day < 1 || day > DateTime.DaysInMonth(year, month)))
            {
                return false;
            }
        }

        date = new DateOnly(year, month, day);
        return true;
    }

    private static bool TryReadTime(ref Cursor reader, out long ticksOfDay)
    {
        ticksOfDay = 0;
        if (!reader.TryReadDigits(2, out var hour) || hour > 23 ||
            !reader.Accept(':') || !reader.TryReadDigits(2, out var minute) || minute > 59 ||
            !reader.Accept(':') || !reader.TryReadDigits(2, out var second) || second > 59)
        {
            return false;
        }

        ticksOfDay = new TimeSpan(hour, minute, second).Ticks;
        if (!reader.Accept('.'))
        {
            return true;
        }

        var digits = reader.ReadDigitRun();
        if (digits.Length == 0 || digits.Length > FractionDigitsKept && digits[FractionDigitsKept..].Any(digit => digit != '0'))
        {
            return false;
        }

        var kept = digits.Length > FractionDigitsKept ? digits[..FractionDigitsKept] : digits.PadRight(FractionDigitsKept, '0');
        ticksOfDay += long.Parse(kept, NumberStyles.None, CultureInfo.InvariantCulture) * TicksPerMicrosecond;
        return true;
    }

    private static bool TryReadOffset(ref Cursor reader, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;
        if (reader.AtEnd || reader.Accept('Z'))
        {
            return true;
        }

        var sign = reader.Accept('+') ? 1 : reader.Accept('-') ? -1 : 0;
        if (sign == 0 ||
            !reader.TryReadDigits(2, out var hours) ||
            !reader.Accept(':') ||
            !reader.TryReadDigits(2, out var minutes) ||
            minutes > 59 ||
            hours > MaxOffsetHours ||
            (hours == MaxOffsetHours && minutes > 0))
        {
            return false;
        }

        offset = new TimeSpan(sign * hours, sign * minutes, 0);
        return true;
    }

    private struct Cursor
    {
        private readonly string _text;
        private int _position;

        public Cursor(string text)
        {
            _text = text;
            _position = 0;
        }

        public readonly bool AtEnd => _position == _text.Length;

        public bool Accept(char expected)
        {
            if (_position < _text.Length && _text[_position] == expected)
            {
                _position++;
                return true;
            }

            return false;
        }

        public bool TryReadDigits(int count, out int value)
        {
            value = 0;
            if (_position + count > _text.Length)
            {
                return false;
            }

            for (var index = 0; index < count; index++)
            {
                var character = _text[_position + index];
                if (!char.IsAsciiDigit(character))
                {
                    return false;
                }

                value = (value * 10) + (character - '0');
            }

            _position += count;
            return true;
        }

        public string ReadDigitRun()
        {
            var start = _position;
            while (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
            {
                _position++;
            }

            return _text[start.._position];
        }
    }
}

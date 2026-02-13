// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text;
using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Processors;

namespace Ignixa.Anonymizer.Tools;

/// <summary>
/// Utilities for redacting and date-shifting FHIR date, dateTime, and instant values.
/// </summary>
internal static class DateTimeTool
{
    private static readonly int YearIndex = 1;
    private static readonly int MonthIndex = 5;
    private static readonly int DayIndex = 7;
    private static readonly int TimeIndex = 8;
    private static readonly int DateShiftSeed = 131;
    private static readonly int DateShiftRange = 50;
    private static readonly int AgeThreshold = 89;

    private static readonly Regex DateRegex = new(
        @"([0-9]([0-9]([0-9][1-9]|[1-9]0)|[1-9]00)|[1-9]000)(-(0[1-9]|1[0-2])(-(0[1-9]|[1-2][0-9]|3[0-1]))?)?",
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex DateTimeRegex = new(
        @"([0-9]([0-9]([0-9][1-9]|[1-9]0)|[1-9]00)|[1-9]000)(-(0[1-9]|1[0-2])(-(0[1-9]|[1-2][0-9]|3[0-1])(T([01][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)(\.[0-9]+)?(Z|(\+|-)((0[0-9]|1[0-3]):[0-5][0-9]|14:00)))?)?)?",
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex TimeRegex = new(@"([01][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)(\.[0-9]+)?");

    public readonly record struct RedactResult(bool WasModified, string OperationType);

    public static RedactResult RedactDateNode(IElement node, bool enablePartialDatesForRedact = false)
    {
        if (!node.IsDateNode() || string.IsNullOrEmpty(node?.Value?.ToString()))
        {
            return new RedactResult(false, AnonymizationOperations.Redact);
        }

        if (enablePartialDatesForRedact)
        {
            var matchedGroups = DateRegex.Match(node.Value.ToString()!).Groups;
            if (matchedGroups[YearIndex].Captures.Any())
            {
                string yearOfDate = matchedGroups[YearIndex].Value;
                if (IndicateAgeOverThreshold(matchedGroups))
                {
                    ElementMutationTool.RemoveProperty(node);
                }
                else
                {
                    ElementMutationTool.SetValue(node, yearOfDate);
                }
            }
        }
        else
        {
            ElementMutationTool.RemoveProperty(node);
        }

        return new RedactResult(true, AnonymizationOperations.Redact);
    }

    public static RedactResult RedactDateTimeAndInstantNode(IElement node, bool enablePartialDatesForRedact = false)
    {
        if ((!node.IsDateTimeNode() && !node.IsInstantNode()) ||
            string.IsNullOrEmpty(node?.Value?.ToString()))
        {
            return new RedactResult(false, AnonymizationOperations.Redact);
        }

        if (enablePartialDatesForRedact)
        {
            var matchedGroups = DateTimeRegex.Match(node.Value.ToString()!).Groups;
            if (matchedGroups[YearIndex].Captures.Any())
            {
                string yearOfDateTime = matchedGroups[YearIndex].Value;
                if (IndicateAgeOverThreshold(matchedGroups))
                {
                    ElementMutationTool.RemoveProperty(node);
                }
                else
                {
                    ElementMutationTool.SetValue(node, yearOfDateTime);
                }
            }
        }
        else
        {
            ElementMutationTool.RemoveProperty(node);
        }

        return new RedactResult(true, AnonymizationOperations.Redact);
    }

    public static RedactResult RedactAgeDecimalNode(IElement node, bool enablePartialAgesForRedact = false)
    {
        if (!node.IsAgeDecimalNode(parent: null) || string.IsNullOrEmpty(node?.Value?.ToString()))
        {
            return new RedactResult(false, AnonymizationOperations.Redact);
        }

        if (enablePartialAgesForRedact)
        {
            if (int.Parse(node.Value.ToString()!) > AgeThreshold)
            {
                ElementMutationTool.RemoveProperty(node);
            }
        }
        else
        {
            ElementMutationTool.RemoveProperty(node);
        }

        return new RedactResult(true, AnonymizationOperations.Redact);
    }

    public readonly record struct DateShiftResult(bool WasModified, string OperationType);

    public static DateShiftResult ShiftDateNode(IElement node, string dateShiftKey, string dateShiftKeyPrefix, int? dateShiftFixedOffsetInDays, bool enablePartialDatesForRedact = false)
    {
        if (!node.IsDateNode() || string.IsNullOrEmpty(node?.Value?.ToString()))
        {
            return new DateShiftResult(false, AnonymizationOperations.DateShift);
        }

        var matchedGroups = DateRegex.Match(node.Value.ToString()!).Groups;
        if (matchedGroups[DayIndex].Captures.Any() && !IndicateAgeOverThreshold(matchedGroups))
        {
            int offset = dateShiftFixedOffsetInDays ?? GetDateShiftValue(node, dateShiftKey, dateShiftKeyPrefix);
            ElementMutationTool.SetValue(node, ShiftDateString(node.Value.ToString()!, offset));
            return new DateShiftResult(true, AnonymizationOperations.Perturb);
        }
        else
        {
            var redactResult = RedactDateNode(node, enablePartialDatesForRedact);
            return new DateShiftResult(redactResult.WasModified, redactResult.OperationType);
        }
    }

    public static DateShiftResult ShiftDateTimeAndInstantNode(IElement node, string dateShiftKey, string dateShiftKeyPrefix, int? dateShiftFixedOffsetInDays, bool enablePartialDatesForRedact = false)
    {
        if ((!node.IsDateTimeNode() && !node.IsInstantNode()) ||
            string.IsNullOrEmpty(node?.Value?.ToString()))
        {
            return new DateShiftResult(false, AnonymizationOperations.DateShift);
        }

        var matchedGroups = DateTimeRegex.Match(node.Value.ToString()!).Groups;
        if (matchedGroups[DayIndex].Captures.Any() && !IndicateAgeOverThreshold(matchedGroups))
        {
            int offset = dateShiftFixedOffsetInDays ?? GetDateShiftValue(node, dateShiftKey, dateShiftKeyPrefix);
            if (matchedGroups[TimeIndex].Captures.Any())
            {
                var newDate = ShiftDateString(node.Value.ToString()!, offset);
                var timestamp = matchedGroups[TimeIndex].Value;
                var timeMatch = TimeRegex.Match(timestamp);
                if (timeMatch.Captures.Any())
                {
                    string time = timeMatch.Captures.First().Value;
                    string newTime = Regex.Replace(time, @"\d", "0");
                    timestamp = timestamp.Replace(time, newTime);
                }
                ElementMutationTool.SetValue(node, $"{newDate}{timestamp}");
            }
            else
            {
                ElementMutationTool.SetValue(node, ShiftDateString(node.Value.ToString()!, offset));
            }
            return new DateShiftResult(true, AnonymizationOperations.Perturb);
        }
        else
        {
            var redactResult = RedactDateTimeAndInstantNode(node, enablePartialDatesForRedact);
            return new DateShiftResult(redactResult.WasModified, redactResult.OperationType);
        }
    }

    private static bool IndicateAgeOverThreshold(GroupCollection groups)
    {
        int year = int.Parse(groups[YearIndex].Value);
        int month = groups[MonthIndex].Captures.Any() ? int.Parse(groups[MonthIndex].Value) : 1;
        int day = groups[DayIndex].Captures.Any() ? int.Parse(groups[DayIndex].Value) : 1;
        int age = DateTime.Now.Year - year -
            (DateTime.Now.Month < month || (DateTime.Now.Month == month && DateTime.Now.Day < day) ? 1 : 0);

        return age > AgeThreshold;
    }

    private static int GetDateShiftValue(IElement node, string dateShiftKey, string dateShiftKeyPrefix)
    {
        if (string.IsNullOrEmpty(dateShiftKeyPrefix))
        {
            dateShiftKeyPrefix = TryGetResourceId(node);
        }

        // Use stack allocation for small keys (typical: 64 + 64 = 128 bytes, safe for stack)
        // Maximum realistic combined length: 256 bytes
        Span<byte> buffer = stackalloc byte[512];

        // Encode prefix and key into buffer
        int prefixByteCount = Encoding.UTF8.GetBytes(dateShiftKeyPrefix, buffer);
        int keyByteCount = Encoding.UTF8.GetBytes(dateShiftKey, buffer[prefixByteCount..]);

        // Calculate offset using combined bytes
        int offset = 0;
        var combinedBytes = buffer[..(prefixByteCount + keyByteCount)];
        foreach (byte b in combinedBytes)
        {
            offset = (offset * DateShiftSeed + b) % (2 * DateShiftRange + 1);
        }

        offset -= DateShiftRange;

        return offset;
    }

    private static string TryGetResourceId(IElement node)
    {
        return string.Empty;
    }

    private static bool IsDateTimeWithOffset(string value)
    {
        return value.Contains('T') || value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(value, @"\+\d{2}:\d{2}$") || Regex.IsMatch(value, @"-\d{2}:\d{2}$");
    }

    private static string ShiftDateString(string value, int offset)
    {
        if (IsDateTimeWithOffset(value))
        {
            return DateTimeOffset.Parse(value).AddDays(offset).ToString("yyyy-MM-dd");
        }
        else
        {
            return DateTime.Parse(value).AddDays(offset).ToString("yyyy-MM-dd");
        }
    }
}

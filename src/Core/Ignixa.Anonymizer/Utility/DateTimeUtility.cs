using System.Text;
using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Processors;

namespace Ignixa.Anonymizer.Utility;

public class DateTimeUtility
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

    public static ProcessResult RedactDateNode(IElement node, bool enablePartialDatesForRedact = false)
    {
        var processResult = new ProcessResult();
        if (!node.IsDateNode() || string.IsNullOrEmpty(node?.Value?.ToString()))
        {
            return processResult;
        }

        if (enablePartialDatesForRedact)
        {
            var matchedGroups = DateRegex.Match(node.Value.ToString()!).Groups;
            if (matchedGroups[YearIndex].Captures.Any())
            {
                string yearOfDate = matchedGroups[YearIndex].Value;
                if (IndicateAgeOverThreshold(matchedGroups))
                {
                    ElementMutationHelper.ClearValue(node);
                }
                else
                {
                    ElementMutationHelper.SetValue(node, yearOfDate);
                }
            }
        }
        else
        {
            ElementMutationHelper.ClearValue(node);
        }

        processResult.AddProcessRecord(AnonymizationOperations.Redact, node);
        return processResult;
    }

    public static ProcessResult RedactDateTimeAndInstantNode(IElement node, bool enablePartialDatesForRedact = false)
    {
        var processResult = new ProcessResult();
        if ((!node.IsDateTimeNode() && !node.IsInstantNode()) ||
            string.IsNullOrEmpty(node?.Value?.ToString()))
        {
            return processResult;
        }

        if (enablePartialDatesForRedact)
        {
            var matchedGroups = DateTimeRegex.Match(node.Value.ToString()!).Groups;
            if (matchedGroups[YearIndex].Captures.Any())
            {
                string yearOfDateTime = matchedGroups[YearIndex].Value;
                if (IndicateAgeOverThreshold(matchedGroups))
                {
                    ElementMutationHelper.ClearValue(node);
                }
                else
                {
                    ElementMutationHelper.SetValue(node, yearOfDateTime);
                }
            }
        }
        else
        {
            ElementMutationHelper.ClearValue(node);
        }

        processResult.AddProcessRecord(AnonymizationOperations.Redact, node);
        return processResult;
    }

    public static ProcessResult RedactAgeDecimalNode(IElement node, bool enablePartialAgesForRedact = false)
    {
        var processResult = new ProcessResult();
        if (!node.IsAgeDecimalNode(parent: null) || string.IsNullOrEmpty(node?.Value?.ToString()))
        {
            return processResult;
        }

        if (enablePartialAgesForRedact)
        {
            if (int.Parse(node.Value.ToString()!) > AgeThreshold)
            {
                ElementMutationHelper.ClearValue(node);
            }
        }
        else
        {
            ElementMutationHelper.ClearValue(node);
        }

        processResult.AddProcessRecord(AnonymizationOperations.Redact, node);
        return processResult;
    }

    public static ProcessResult ShiftDateNode(IElement node, string dateShiftKey, string dateShiftKeyPrefix, int? dateShiftFixedOffsetInDays, bool enablePartialDatesForRedact = false)
    {
        var processResult = new ProcessResult();
        if (!node.IsDateNode() || string.IsNullOrEmpty(node?.Value?.ToString()))
        {
            return processResult;
        }

        var matchedGroups = DateRegex.Match(node.Value.ToString()!).Groups;
        if (matchedGroups[DayIndex].Captures.Any() && !IndicateAgeOverThreshold(matchedGroups))
        {
            int offset = dateShiftFixedOffsetInDays ?? GetDateShiftValue(node, dateShiftKey, dateShiftKeyPrefix);
            ElementMutationHelper.SetValue(node, ShiftDateString(node.Value.ToString()!, offset));
            processResult.AddProcessRecord(AnonymizationOperations.Perturb, node);
        }
        else
        {
            processResult = RedactDateNode(node, enablePartialDatesForRedact);
        }

        return processResult;
    }

    public static ProcessResult ShiftDateTimeAndInstantNode(IElement node, string dateShiftKey, string dateShiftKeyPrefix, int? dateShiftFixedOffsetInDays, bool enablePartialDatesForRedact = false)
    {
        var processResult = new ProcessResult();
        if ((!node.IsDateTimeNode() && !node.IsInstantNode()) ||
            string.IsNullOrEmpty(node?.Value?.ToString()))
        {
            return processResult;
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
                ElementMutationHelper.SetValue(node, $"{newDate}{timestamp}");
            }
            else
            {
                ElementMutationHelper.SetValue(node, ShiftDateString(node.Value.ToString()!, offset));
            }
            processResult.AddProcessRecord(AnonymizationOperations.Perturb, node);
        }
        else
        {
            processResult = RedactDateTimeAndInstantNode(node, enablePartialDatesForRedact);
        }

        return processResult;
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

        int offset = 0;
        var bytes = Encoding.UTF8.GetBytes(dateShiftKeyPrefix + dateShiftKey);
        foreach (byte b in bytes)
        {
            offset = (offset * DateShiftSeed + b) % (2 * DateShiftRange + 1);
        }

        offset -= DateShiftRange;

        return offset;
    }

    private static string TryGetResourceId(IElement node)
    {
        // In the old Firely SDK, this walked up via Parent to find the resource root.
        // IElement has no Parent. The resource id should be passed via ProcessContext.ResourceId
        // through DateShiftProcessor. This is only a fallback for direct utility calls.
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

using System.Text.Json;
using System.Text.Json.Nodes;
using EnsureThat;
using Ignixa.FhirPath.Parser;
using Ignixa.Anonymizer.Exceptions;

namespace Ignixa.Anonymizer.Processors.Settings;

public class GeneralizeSetting
{
    public JsonObject Cases { get; set; } = [];
    public GeneralizationOtherValuesOperation OtherValues { get; set; }

    private static readonly FhirPathParser Parser = new();

    public static GeneralizeSetting CreateFromRuleSettings(Dictionary<string, object> ruleSettings)
    {
        EnsureArg.IsNotNull(ruleSettings);

        var casesStr = ruleSettings.GetValueOrDefault(RuleKeys.Cases)?.ToString();
        var cases = JsonNode.Parse(casesStr!)?.AsObject() ?? [];

        if (!Enum.TryParse<GeneralizationOtherValuesOperation>(
                ruleSettings.GetValueOrDefault(RuleKeys.OtherValues)?.ToString(), true, out var otherValues))
        {
            otherValues = GeneralizationOtherValuesOperation.Redact;
        }

        return new GeneralizeSetting
        {
            OtherValues = otherValues,
            Cases = cases
        };
    }

    public static void ValidateRuleSettings(Dictionary<string, object> ruleSettings)
    {
        if (ruleSettings is null)
        {
            throw new AnonymizerConfigurationException("Generalize rule should not be null.");
        }

        if (!ruleSettings.ContainsKey(Constants.PathKey))
        {
            throw new AnonymizerConfigurationException("Missing path in FHIR path rule config.");
        }

        if (!ruleSettings.ContainsKey(Constants.MethodKey))
        {
            throw new AnonymizerConfigurationException("Missing method in FHIR path rule config.");
        }

        if (!ruleSettings.ContainsKey(RuleKeys.Cases))
        {
            throw new AnonymizerConfigurationException("Missing cases in FHIR path rule config.");
        }

        ValidateCases(ruleSettings);

        var supportedOtherValuesOperations = Enum.GetNames(typeof(GeneralizationOtherValuesOperation))
            .ToHashSet(StringComparer.InvariantCultureIgnoreCase);
        if (ruleSettings.ContainsKey(RuleKeys.OtherValues) &&
            !supportedOtherValuesOperations.Contains(ruleSettings[RuleKeys.OtherValues].ToString()!))
        {
            throw new AnonymizerConfigurationException($"OtherValues setting is invalid at {ruleSettings[RuleKeys.OtherValues]}.");
        }
    }

    private static void ValidateCases(Dictionary<string, object> ruleSettings)
    {
        JsonObject cases;
        try
        {
            var casesStr = ruleSettings.GetValueOrDefault(RuleKeys.Cases)?.ToString();
            cases = JsonNode.Parse(casesStr!)?.AsObject()
                ?? throw new JsonException("Cases is null");
        }
        catch (JsonException ex)
        {
            throw new AnonymizerConfigurationException(
                $"Invalid Json format {ruleSettings.GetValueOrDefault(RuleKeys.Cases)}", ex);
        }

        foreach (var (key, value) in cases)
        {
            try
            {
                Parser.Parse(key);
                Parser.Parse(value?.ToString() ?? string.Empty);
            }
            catch (Exception ex)
            {
                throw new AnonymizerConfigurationException($"Invalid cases expression {key}: {value}", ex);
            }
        }
    }
}

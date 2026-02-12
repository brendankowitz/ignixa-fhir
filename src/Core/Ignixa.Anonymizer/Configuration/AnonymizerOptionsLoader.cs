// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Anonymizer.Models;

namespace Ignixa.Anonymizer.Configuration;

/// <summary>
/// Utility for loading anonymizer configuration from JSON files or strings.
/// </summary>
public static class AnonymizerOptionsLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Loads anonymizer options from a JSON configuration file.
    /// </summary>
    public static Result<AnonymizerOptions> LoadFromFile(string configFilePath)
    {
        try
        {
            if (!File.Exists(configFilePath))
            {
                return Result<AnonymizerOptions>.Failure(new AnonymizerError(
                    "CONFIG_NOT_FOUND",
                    $"Configuration file not found: {configFilePath}"));
            }

            var json = File.ReadAllText(configFilePath);
            return LoadFromJson(json);
        }
        catch (Exception ex)
        {
            return Result<AnonymizerOptions>.Failure(new AnonymizerError(
                "CONFIG_READ_ERROR",
                $"Failed to read configuration file: {ex.Message}",
                Exception: ex));
        }
    }

    /// <summary>
    /// Loads anonymizer options from a JSON string.
    /// </summary>
    public static Result<AnonymizerOptions> LoadFromJson(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            if (root is null)
            {
                return Result<AnonymizerOptions>.Failure(new AnonymizerError(
                    "CONFIG_PARSE_ERROR",
                    "Configuration JSON is empty or invalid"));
            }

            var fhirVersion = root["fhirVersion"]?.GetValue<string>() ?? string.Empty;

            var rulesArray = root["fhirPathRules"]?.AsArray();
            if (rulesArray is null || rulesArray.Count == 0)
            {
                return Result<AnonymizerOptions>.Failure(new AnonymizerError(
                    "CONFIG_NO_RULES",
                    "Configuration must specify at least one 'fhirPathRules' entry"));
            }

            var rules = ParseRules(rulesArray);
            var parameters = ParseParameters(root["parameters"]);
            var processing = ParseProcessing(root["processing"]);

            var options = new AnonymizerOptions
            {
                FhirVersion = fhirVersion,
                Rules = rules,
                Parameters = parameters,
                Processing = processing
            };

            return Result<AnonymizerOptions>.Success(options);
        }
        catch (JsonException ex)
        {
            return Result<AnonymizerOptions>.Failure(new AnonymizerError(
                "CONFIG_JSON_ERROR",
                $"Invalid JSON in configuration: {ex.Message}",
                Exception: ex));
        }
        catch (Exception ex)
        {
            return Result<AnonymizerOptions>.Failure(new AnonymizerError(
                "CONFIG_PARSE_ERROR",
                $"Failed to parse configuration: {ex.Message}",
                Exception: ex));
        }
    }

    private static ImmutableArray<FhirPathRule> ParseRules(JsonArray rulesArray)
    {
        var builder = ImmutableArray.CreateBuilder<FhirPathRule>(rulesArray.Count);

        foreach (var ruleNode in rulesArray)
        {
            if (ruleNode is null) continue;

            var path = ruleNode["path"]?.GetValue<string>();
            var method = ruleNode["method"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(method))
            {
                continue;
            }

            var resourceType = ruleNode["resourceType"]?.GetValue<string>();
            var settings = ParseSettings(ruleNode);

            builder.Add(new FhirPathRule
            {
                Path = path,
                Method = method.ToUpperInvariant(),
                ResourceType = resourceType,
                Settings = settings
            });
        }

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<string, object>? ParseSettings(JsonNode? settingsNode)
    {
        if (settingsNode is null) return null;

        var dict = ImmutableDictionary.CreateBuilder<string, object>();

        if (settingsNode is JsonObject obj)
        {
            foreach (var kvp in obj)
            {
                if (kvp.Value is null) continue;

                object? value = kvp.Value switch
                {
                    JsonValue jv when jv.TryGetValue<string>(out var s) => s,
                    JsonValue jv when jv.TryGetValue<int>(out var i) => i,
                    JsonValue jv when jv.TryGetValue<bool>(out var b) => b,
                    JsonValue jv when jv.TryGetValue<double>(out var d) => d,
                    _ => kvp.Value.ToJsonString()
                };

                if (value is not null)
                {
                    dict[kvp.Key] = value;
                }
            }
        }

        return dict.Count > 0 ? dict.ToImmutable() : null;
    }

    private static ParameterOptions? ParseParameters(JsonNode? parametersNode)
    {
        if (parametersNode is null) return null;

        return parametersNode.Deserialize<ParameterOptions>(JsonOptions);
    }

    private static ProcessingOptions? ParseProcessing(JsonNode? processingNode)
    {
        if (processingNode is null) return null;

        return processingNode.Deserialize<ProcessingOptions>(JsonOptions);
    }
}

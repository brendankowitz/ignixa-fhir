// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Views;
using Ignixa.DeId.Configuration;
using Ignixa.DeId.Models;
using Ignixa.FhirPath.Parser;

namespace Ignixa.CliOnFhir.DeId;

/// <summary>
/// An <c>Ignixa.DeId</c> configuration, read and checked before anything is de-identified. A configuration can hold secrets (the
/// <c>cryptoHash</c>, <c>dateShift</c> and <c>encrypt</c> keys), and its rules can come from a file in the data folder, so no value of
/// it reaches a message: a problem names the rule by its position and method and says what is wrong, and a JSON error gives only its
/// line and byte. Every FHIRPath expression in it (rule paths, <c>generalize</c> cases) is held to the same lexical limits as a
/// ViewDefinition's (<see cref="ViewExpressionGuards"/>) and parsed, so that no rule can overflow the stack or fail on every
/// resource. Where <c>Ignixa.DeId</c> would quietly make up a random key (a method whose key is missing), the configuration is refused
/// instead: the library only logs a warning, which this tool does not show, and the output could not be reproduced or linked to
/// another run's.
/// </summary>
/// <param name="Options">The options the engine is built with.</param>
/// <param name="Facts">The configuration's shape, as reported.</param>
/// <param name="Notes">Warnings and information about what the configuration will do.</param>
internal sealed record DeIdConfiguration(DeIdOptions Options, DeIdConfigFacts Facts, IReadOnlyList<IssueItem> Notes)
{
    /// <summary>The largest configuration read.</summary>
    public const int MaxBytes = 1024 * 1024;

    private const string InvalidCode = "deid-config-invalid";

    // Methods whose output differs on every run whatever the keys.
    private static readonly string[] RandomMethods = ["perturb", "encrypt"];

    // The methods Ignixa.DeId registers a processor for, by the name its documentation spells them with.
    private static readonly Dictionary<string, string> Methods = new(StringComparer.OrdinalIgnoreCase)
    {
        ["redact"] = "redact",
        ["dateShift"] = "dateShift",
        ["cryptoHash"] = "cryptoHash",
        ["encrypt"] = "encrypt",
        ["substitute"] = "substitute",
        ["perturb"] = "perturb",
        ["keep"] = "keep",
        ["generalize"] = "generalize",
    };

    /// <summary>
    /// The name a method, or an operation <c>Ignixa.DeId</c> counts, is reported by: as its documentation spells it, whatever case it was
    /// given or counted in. The library also counts <c>abstract</c> (a value redacted in part, such as a date cut to its year).
    /// </summary>
    public static string MethodName(string method) =>
        Methods.TryGetValue(method, out string? name) ? name : string.Equals(method, "ABSTRACT", StringComparison.OrdinalIgnoreCase) ? "abstract" : method;

    /// <summary>Reads <paramref name="config"/>, inline JSON or a file path, and checks it.</summary>
    /// <exception cref="ToolRequestException">
    /// <c>deid-config-required</c> (none given), <c>deid-config-unreadable</c> (the file), <c>deid-config-invalid</c> (its JSON or a
    /// rule), <c>deid-config-key-missing</c> (a method without its key).
    /// </exception>
    public static DeIdConfiguration Load(string? config, IFhirSchemaProvider schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (string.IsNullOrWhiteSpace(config))
        {
            throw ToolRequestException.Error("deid-config-required", "deid needs a configuration ('config': a file, or inline JSON): Ignixa.DeId has no default profile that is safe for a whole folder (its DARTS Safe Harbor bootstrap policy only touches Patient elements and has no keys), so one is never assumed. See configuration-sample.json in tools/Ignixa.DeId.Cli for a starting point, and set its keys.");
        }

        (string text, string source, string? file) = Read(config);
        JsonObject root = Parse(text);
        List<DeIdRuleFacts> rules = CheckRules(root);
        Result<DeIdOptions> loaded = DeIdOptionsLoader.LoadFromJson(text);
        if (!loaded.IsSuccess)
        {
            throw ToolRequestException.Error(InvalidCode, $"Ignixa.DeId could not read the configuration ({Code(loaded.Error.Code)}); its message is not repeated because it can quote the configuration.");
        }

        DeIdOptions options = loaded.Value;
        CheckKeys(options, rules);
        return new DeIdConfiguration(options, new DeIdConfigFacts(source, file, rules), NotesOf(options, rules, schema));
    }

    // Text that holds any of these is never a path: it is inline configuration, and so never repeated.
    private static readonly char[] InlineMarks = ['{', '[', '"', '\n', '\r'];

    // A path is repeated by its digest only: text taken for one may be a piece of configuration that was meant inline.
    private static (string Text, string Source, string? File) Read(string config)
    {
        string text = config.TrimStart('\uFEFF');
        if (text.IndexOfAny(InlineMarks) >= 0)
        {
            return Encoding.UTF8.GetByteCount(text) <= MaxBytes
                ? (text, "inline", null)
                : throw ToolRequestException.Error(InvalidCode, $"The inline configuration is larger than {MaxBytes} bytes.");
        }

        string named = $"The configuration file given (digest {FhirIds.Digest(config)})";
        try
        {
            string path = Path.GetFullPath(text);
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                throw ToolRequestException.Error("deid-config-unreadable", $"{named} does not exist; give a file, or inline JSON. The path is not repeated, in case it was configuration meant inline.");
            }

            if (info.Length > MaxBytes)
            {
                throw ToolRequestException.Error(InvalidCode, $"{named} is larger than {MaxBytes} bytes.");
            }

            return (File.ReadAllText(path), "file", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw ToolRequestException.Error("deid-config-unreadable", $"{named} cannot be read ({ex.GetType().Name}).");
        }
    }

    // A JSON error is located, never quoted: System.Text.Json's message repeats the text it stopped at.
    private static JsonObject Parse(string text)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            string where = ex.LineNumber is long line ? $" at line {line + 1}, byte {ex.BytePositionInLine + 1}" : string.Empty;
            throw ToolRequestException.Error(InvalidCode, $"The configuration is not valid JSON{where}.");
        }

        return root as JsonObject ?? throw ToolRequestException.Error(InvalidCode, "The configuration must be a JSON object.");
    }

    // Ignixa.DeId skips a rule without a path or a method without a word; here it is an error, since a rule left out leaves data in.
    private static List<DeIdRuleFacts> CheckRules(JsonObject root)
    {
        if (root["fhirPathRules"] is not JsonArray entries || entries.Count == 0)
        {
            throw ToolRequestException.Error(InvalidCode, "The configuration has no 'fhirPathRules': at least one rule is needed.");
        }

        List<DeIdRuleFacts> rules = [];
        for (int i = 0; i < entries.Count; i++)
        {
            string method = CheckRule(entries[i], i);
            int index = rules.FindIndex(rule => rule.Method == method);
            if (index < 0)
            {
                rules.Add(new DeIdRuleFacts(method, 1));
            }
            else
            {
                rules[index] = rules[index] with { Paths = rules[index].Paths + 1 };
            }
        }

        return rules;
    }

    private static string CheckRule(JsonNode? entry, int index)
    {
        string label = $"fhirPathRules[{index}]";
        if (entry is not JsonObject rule || Text(rule["path"]) is not { } path || Text(rule["method"]) is not { } given)
        {
            throw ToolRequestException.Error(InvalidCode, $"{label} must be an object with a text 'path' and a text 'method'; Ignixa.DeId would skip it and leave the data it was for as it is.");
        }

        if (!Methods.TryGetValue(given, out string? method))
        {
            throw ToolRequestException.Error(InvalidCode, $"{label} names a method Ignixa.DeId does not have (it is not repeated); the methods are {string.Join(", ", Methods.Values)}.");
        }

        if (rule["resourceType"] is { } type && Text(type) is null)
        {
            throw ToolRequestException.Error(InvalidCode, $"{label} ({method}) has a 'resourceType' that is not text.");
        }

        CheckExpression(path, $"{label} ({method}): its path");
        if (method == "generalize")
        {
            CheckCases(rule["cases"], label);
        }

        return method;
    }

    private static void CheckCases(JsonNode? cases, string label)
    {
        if (cases is not JsonObject expressions || expressions.Count == 0)
        {
            throw ToolRequestException.Error(InvalidCode, $"{label} (generalize) needs 'cases', an object of FHIRPath conditions and the FHIRPath values they give.");
        }

        int number = 0;
        foreach ((string condition, JsonNode? value) in expressions)
        {
            number++;
            CheckExpression(condition, $"{label} (generalize): the condition of case {number}");
            CheckExpression(Text(value) ?? throw ToolRequestException.Error(InvalidCode, $"{label} (generalize): the value of case {number} is not a FHIRPath expression in text."), $"{label} (generalize): the value of case {number}");
        }
    }

    private static void CheckExpression(string expression, string what)
    {
        if (ViewExpressionGuards.Violation(expression) is { } reason)
        {
            throw ToolRequestException.Error(InvalidCode, $"{what} {reason}.");
        }

        try
        {
            _ = new FhirPathParser().Parse(expression);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or NotSupportedException or OverflowException)
        {
            throw ToolRequestException.Error(InvalidCode, $"{what} is not valid FHIRPath ({ex.GetType().Name}); the expression is not repeated.");
        }
    }

    private static void CheckKeys(DeIdOptions options, List<DeIdRuleFacts> rules)
    {
        ParameterOptions? parameters = options.Parameters;
        if (Uses(rules, "cryptoHash") && string.IsNullOrWhiteSpace(parameters?.CryptoHashKey))
        {
            throw KeyMissing("cryptoHash", "parameters.cryptoHashKey");
        }

        if (Uses(rules, "dateShift") && string.IsNullOrWhiteSpace(parameters?.DateShiftKey) && parameters?.DateShiftFixedOffsetInDays is null)
        {
            throw KeyMissing("dateShift", "parameters.dateShiftKey (or parameters.dateShiftFixedOffsetInDays)");
        }

        if (!Uses(rules, "encrypt"))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(parameters?.EncryptKey))
        {
            throw KeyMissing("encrypt", "parameters.encryptKey");
        }

        if (Encoding.UTF8.GetByteCount(parameters.EncryptKey) is not (16 or 24 or 32))
        {
            throw ToolRequestException.Error(InvalidCode, "parameters.encryptKey must be 16, 24 or 32 bytes in UTF-8 (an AES-128, -192 or -256 key); it is not repeated.");
        }
    }

    private static ToolRequestException KeyMissing(string method, string parameter) =>
        ToolRequestException.Error("deid-config-key-missing", $"Rules use {method}, but {parameter} is not set. Ignixa.DeId would make up a random key, so the output would differ on every run and could not be linked with another run's; set a key of your own.");

    private static List<IssueItem> NotesOf(DeIdOptions options, List<DeIdRuleFacts> rules, IFhirSchemaProvider schema)
    {
        List<IssueItem> notes = [];
        if (!string.IsNullOrEmpty(options.FhirVersion) && !string.Equals(options.FhirVersion, schema.Version.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            notes.Add(new IssueItem("warning", "deid-config-version", $"The configuration's fhirVersion is not the folder's {schema.Version}; its rules were applied to {schema.Version} resources as written, so a path that names an element of another version matches nothing.", Location: null));
        }

        string[] random = [.. RandomMethods.Where(method => Uses(rules, method))];
        if (random.Length > 0)
        {
            string verb = random.Length == 1 ? "gives" : "give";
            notes.Add(new IssueItem("information", "deid-not-reproducible", $"{string.Join(" and ", random)} {verb} a different value on every run (perturb adds random noise; encrypt uses a random initialization vector), so two runs of this configuration do not write the same files.", Location: null));
        }

        return notes;
    }

    private static bool Uses(List<DeIdRuleFacts> rules, string method) => rules.Exists(rule => rule.Method == method);

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    // Ignixa.DeId's error codes are constants such as CONFIG_JSON_ERROR; anything else is shown as a digest.
    private static string Code(string code) =>
        code.Length <= 40 && code.All(c => c is (>= 'A' and <= 'Z') or '_') ? code : FhirIds.Digest(code);
}

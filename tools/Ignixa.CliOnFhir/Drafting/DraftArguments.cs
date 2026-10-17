// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>
/// One object of a draft's typed input, read strictly: each member is asked for by name and type, and <see cref="Done"/> refuses any
/// member that was not asked for, naming the ones that may be given. The input is the caller's text, and it becomes resource
/// content, so no message repeats a value of it: problems name the member by its path (the tool's own names), and a member
/// name the caller made up is shown only when it is shaped like a name, else by its digest.
/// </summary>
internal sealed partial class DraftArguments
{
    /// <summary>The error code of input that is not what the schema asks for.</summary>
    public const string InvalidCode = "draft-input-invalid";

    /// <summary>The longest text a member may hold.</summary>
    public const int MaxTextLength = 4096;

    private readonly JsonObject _json;
    private readonly string _path;
    private readonly List<string> _asked = [];

    /// <param name="json">The object.</param>
    /// <param name="path">Its path in the input, for messages: <c>input</c>, <c>input.medication</c>, ...</param>
    public DraftArguments(JsonObject json, string path)
    {
        _json = json;
        _path = path;
    }

    /// <summary>The object's path in the input.</summary>
    public string Path => _path;

    /// <summary>A text member, or null when it is absent; never empty, never only white space.</summary>
    public string? OptionalString(string name)
    {
        if (Member(name) is not { } node)
        {
            return null;
        }

        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.String)
        {
            throw Invalid($"'{Of(name)}' must be text.");
        }

        string text = value.GetValue<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw Invalid($"'{Of(name)}' must not be empty; leave it out instead.");
        }

        return text.Length <= MaxTextLength ? text : throw Invalid($"'{Of(name)}' is longer than {MaxTextLength} characters.");
    }

    /// <summary>A text member that must be given.</summary>
    public string RequiredString(string name) => OptionalString(name) ?? throw Missing(name);

    /// <summary>A number member, or null when it is absent; it is kept as written (a FHIR decimal keeps its precision).</summary>
    public decimal? OptionalNumber(string name)
    {
        if (Member(name) is not { } node)
        {
            return null;
        }

        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.Number || !decimal.TryParse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number))
        {
            throw Invalid($"'{Of(name)}' must be a number.");
        }

        return number;
    }

    /// <summary>A number member that must be given and be greater than zero.</summary>
    public decimal RequiredPositiveNumber(string name)
    {
        decimal number = OptionalNumber(name) ?? throw Missing(name);
        return number > 0 ? number : throw Invalid($"'{Of(name)}' must be greater than 0.");
    }

    /// <summary>A whole-number member that must be given and be at least 1.</summary>
    public int RequiredPositiveInteger(string name)
    {
        decimal number = OptionalNumber(name) ?? throw Missing(name);
        return number >= 1 && number <= int.MaxValue && decimal.Truncate(number) == number ? (int)number : throw Invalid($"'{Of(name)}' must be a whole number of at least 1.");
    }

    /// <summary>An object member, or null when it is absent.</summary>
    public DraftArguments? OptionalObject(string name)
    {
        if (Member(name) is not { } node)
        {
            return null;
        }

        return node is JsonObject json ? new DraftArguments(json, Of(name)) : throw Invalid($"'{Of(name)}' must be an object.");
    }

    /// <summary>An object member that must be given.</summary>
    public DraftArguments RequiredObject(string name) => OptionalObject(name) ?? throw Missing(name);

    /// <summary>An array of text, empty when absent.</summary>
    public IReadOnlyList<string> StringArray(string name) => Items(name).Select((item, index) =>
        item is JsonValue value && value.GetValueKind() == JsonValueKind.String && value.GetValue<string>() is { Length: > 0 and <= MaxTextLength } text && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw Invalid($"'{Of(name)}[{index}]' must be text of 1 to {MaxTextLength} characters.")).ToList();

    /// <summary>An array of objects, empty when absent.</summary>
    public IReadOnlyList<DraftArguments> ObjectArray(string name) => Items(name).Select((item, index) =>
        item is JsonObject json ? new DraftArguments(json, $"{Of(name)}[{index}]") : throw Invalid($"'{Of(name)}[{index}]' must be an object.")).ToList();

    /// <summary>Refuses a member that was not asked for: the schema has no such member, so it would be dropped unseen.</summary>
    /// <exception cref="ToolRequestException"><see cref="InvalidCode"/>, naming the members that may be given and the closest one.</exception>
    public void Done()
    {
        if (_json.Select(pair => pair.Key).FirstOrDefault(key => !_asked.Contains(key, StringComparer.Ordinal)) is not { } unknown)
        {
            return;
        }

        string shown = Name().IsMatch(unknown) ? $"'{unknown}'" : $"(digest {FhirIds.Digest(unknown)})";
        string hint = Closest(unknown) is { } closest ? $" Did you mean '{closest}'?" : string.Empty;
        throw Invalid($"{_path} has a member {shown} that a draft does not take.{hint} {_path} takes: {string.Join(", ", _asked)}.");
    }

    /// <summary>The error for input that is not what the schema asks for.</summary>
    public static ToolRequestException Invalid(string diagnostics) => ToolRequestException.Error(InvalidCode, diagnostics);

    /// <summary>The path of member <paramref name="name"/> of this object.</summary>
    public string Of(string name) => $"{_path}.{name}";

    private ToolRequestException Missing(string name) => Invalid($"'{Of(name)}' is required.");

    private JsonNode? Member(string name)
    {
        _asked.Add(name);
        if (!_json.TryGetPropertyValue(name, out JsonNode? node))
        {
            return null;
        }

        return node ?? throw Invalid($"'{Of(name)}' is null; leave it out instead.");
    }

    private JsonArray Items(string name)
    {
        if (Member(name) is not { } node)
        {
            return [];
        }

        return node as JsonArray ?? throw Invalid($"'{Of(name)}' must be an array.");
    }

    // The member that may be given that the unknown one most likely meant: the same ignoring case, or at most two edits away.
    private string? Closest(string unknown) => _asked
        .Select(name => (Name: name, Distance: string.Equals(name, unknown, StringComparison.OrdinalIgnoreCase) ? 0 : Distance(name.ToUpperInvariant(), unknown.ToUpperInvariant())))
        .Where(candidate => candidate.Distance <= 2)
        .OrderBy(candidate => candidate.Distance)
        .Select(candidate => candidate.Name)
        .FirstOrDefault();

    // Levenshtein distance, cut short past 2 (only near names are suggested), over member names of at most 64 characters.
    private static int Distance(string left, string right)
    {
        if (Math.Abs(left.Length - right.Length) > 2 || right.Length > 64)
        {
            return 3;
        }

        int[] previous = [.. Enumerable.Range(0, right.Length + 1)];
        for (int i = 1; i <= left.Length; i++)
        {
            int[] current = new int[right.Length + 1];
            current[0] = i;
            for (int j = 1; j <= right.Length; j++)
            {
                int substitution = previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j] + 1, current[j - 1] + 1));
            }

            previous = current;
        }

        return previous[right.Length];
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Name();
}

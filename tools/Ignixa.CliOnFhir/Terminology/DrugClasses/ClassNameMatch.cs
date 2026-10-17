// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace Ignixa.CliOnFhir.Terminology.DrugClasses;

/// <summary>
/// Matches search text against drug class names, word by word: each word of the text (case- and accent-folded, with
/// a plural 's' and an 'er'/'ing' ending removed) must start a word of the name, so "beta blocker" matches "Beta blocking
/// agents" and "beta-Adrenergic Blocker". Formal class names never say "statin" or "SSRI", so a few common short names
/// are also searched as the formal words they stand for.
/// </summary>
internal sealed partial class ClassNameMatch
{
    public const int Exact = 0;
    public const int Leading = 1;
    public const int AllWords = 2;
    public const int NoMatch = -1;

    private static readonly IReadOnlyDictionary<string, string[]> Aliases = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [Key("statin")] = ["HMG CoA reductase inhibitor"],
        [Key("SSRI")] = ["selective serotonin reuptake inhibitor"],
        [Key("PPI")] = ["proton pump inhibitor"],
        [Key("NSAID")] = ["antiinflammatory non steroid", "nonsteroidal anti inflammatory"],
        [Key("blood thinner")] = ["anticoagulant"],
    };

    private readonly IReadOnlyList<(string Folded, string[] Words)> _alternatives;

    private ClassNameMatch(IReadOnlyList<(string Folded, string[] Words)> alternatives, string? alias)
    {
        _alternatives = alternatives;
        SearchedAs = alias;
    }

    /// <summary>The formal words searched in place of the text, when it was a known short name; otherwise null.</summary>
    public string? SearchedAs { get; }

    public static ClassNameMatch For(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        (string Folded, string[] Words) own = Prepare(text);
        return Aliases.TryGetValue(string.Join(' ', own.Words), out string[]? formal)
            ? new ClassNameMatch([own, .. formal.Select(Prepare)], string.Join(" | ", formal))
            : new ClassNameMatch([own], null);
    }

    /// <summary><see cref="Exact"/>, <see cref="Leading"/> (the words start the name), <see cref="AllWords"/>, or <see cref="NoMatch"/>.</summary>
    public int Score(string? name)
    {
        if (name is null)
        {
            return NoMatch;
        }

        (string folded, string[] words) = Prepare(name);
        int best = NoMatch;
        foreach ((string text, string[] query) in _alternatives)
        {
            int score = query.Length == 0 ? NoMatch
                : folded == text ? Exact
                : query.Length <= words.Length && query.Select((word, index) => words[index].StartsWith(word, StringComparison.Ordinal)).All(match => match) ? Leading
                : query.All(word => words.Any(candidate => candidate.StartsWith(word, StringComparison.Ordinal))) ? AllWords
                : NoMatch;
            best = score != NoMatch && (best == NoMatch || score < best) ? score : best;
        }

        return best;
    }

    private static string Key(string text) => string.Join(' ', Prepare(text).Words);

    private static (string Folded, string[] Words) Prepare(string text)
    {
        string folded = TextFolding.Fold(text);
        string[] words = [.. NonWord().Split(folded).Where(word => word.Length > 0).Select(Stem)];
        return (string.Join(' ', words), words);
    }

    private static string Stem(string word)
    {
        string stem = word.Length > 3 && word.EndsWith('S') && !word.EndsWith("SS", StringComparison.Ordinal) ? word[..^1] : word;
        return stem.Length > 5 && stem.EndsWith("ING", StringComparison.Ordinal) ? stem[..^3]
            : stem.Length > 4 && stem.EndsWith("ER", StringComparison.Ordinal) ? stem[..^2]
            : stem;
    }

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+")]
    private static partial Regex NonWord();
}

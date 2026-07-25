// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// Guards the conformance corpus against extension drift (ADR 2607). Unknown non-modifier
/// extensions are silently ignored per the FHIR spec, so a suite authored against an engine
/// capability that does not ship would pass parsing and simply not do what its author
/// intended. This asserts every Ignixa-canonical extension URL used by a suite is one the
/// engine actually implements.
/// </summary>
public partial class ConformanceSuiteExtensionGuardTests
{
    private const string IgnixaExtensionPrefix = "http://ignixa.io/testscript/";
    private const string RequiresCapabilityUrl = "http://ignixa.io/testscript/requiresCapability";

    private static readonly string[] SearchResultParameters =
        ["_sort", "_count", "_total", "_summary", "_elements", "_include", "_revinclude", "_contained"];

    // ADR 2607 documents the first four. The last three landed after it was written
    // (assertionAnyOfGroup and assertionWhenResponseStatus in PR #330, waitFor separately)
    // and are implemented in Ignixa.TestScript, so suites may legitimately use them.
    // Adding a URL here without a corresponding engine implementation defeats this guard.
    private static readonly HashSet<string> KnownExtensionUrls = new(StringComparer.Ordinal)
    {
        "http://ignixa.io/testscript/parametrize",
        "http://ignixa.io/testscript/fhirVersions",
        "http://ignixa.io/testscript/requiresCapability",
        "http://ignixa.io/testscript/fhirfakes",
        "http://ignixa.io/testscript/assertionAnyOfGroup",
        "http://ignixa.io/testscript/assertionWhenResponseStatus",
        "http://ignixa.io/testscript/waitFor",
    };

    [Fact]
    public void GivenConformanceSuites_WhenReadingExtensionUrls_ThenAllAreImplementedByTheEngine()
    {
        var suiteFiles = EnumerateSuiteFiles().ToList();
        suiteFiles.ShouldNotBeEmpty("Expected to find conformance suites; scan path may be wrong.");

        var unknown = suiteFiles
            .SelectMany(file => CollectIgnixaExtensionUrls(file).Select(url => (file, url)))
            .Where(pair => !KnownExtensionUrls.Contains(pair.url))
            .Select(pair => $"{Path.GetFileName(pair.file)}: {pair.url}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        unknown.ShouldBeEmpty(
            "A suite uses an Ignixa TestScript extension the engine does not implement (ADR 2607). " +
            $"Known URLs: {string.Join(", ", KnownExtensionUrls)}. " +
            "Unknown non-modifier extensions are silently ignored, so this would not fail at runtime — " +
            "either implement the extension in Ignixa.TestScript and add it here, or fix the suite.");
    }

    [Fact]
    public void GivenConformanceSuites_WhenReadingCapabilityGates_ThenNoneAreStructurallyUnsatisfiable()
    {
        var gates = EnumerateSuiteFiles()
            .SelectMany(file => CollectIgnixaExtensions(file)
                .Where(extension => extension.Url == RequiresCapabilityUrl
                                    && !string.IsNullOrEmpty(extension.ValueString))
                .Select(extension => (file, expression: extension.ValueString!)))
            .ToList();

        // Without this the guard passes green on an empty input set — if RequiresCapabilityUrl
        // drifts, or the value stops living on valueString, it would report no offenders forever.
        // That is the same can't-fail mode the guard exists to prevent.
        gates.ShouldNotBeEmpty(
            $"Found no {RequiresCapabilityUrl} extensions carrying a valueString. Either the corpus " +
            "moved, or the extension URL or value element changed and this guard is now inert.");

        var offenders = gates
            .SelectMany(pair => DescribeUnsatisfiableClauses(pair.expression)
                .Select(reason => $"{Path.GetFileName(pair.file)}: {reason}"))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "A requiresCapability gate that can never evaluate true silently skips its test forever, " +
            "reporting neither pass nor fail. Search result parameters are not SearchParameter " +
            "resources, so no conformant server advertises them in CapabilityStatement.searchParam. " +
            "Gate on the resource-level interaction and the ordinary search parameter codes instead.");
    }

    // Flags result parameters only: those are unsatisfiable against any conformant server, whereas a
    // rest-level gate on an ordinary parameter is merely unsatisfiable against Ignixa — whose metadata
    // pipeline never populates that collection — and always sits on the or-branch of a disjunction.
    // Catches result-parameter gates at rest level and resource level alike. Not airtight: it matches
    // source text, so a reversed or restructured spelling still slips through.
    private static IEnumerable<string> DescribeUnsatisfiableClauses(string expression)
    {
        // FHIRPath tolerates whitespace around '=' and the corpus already uses both spellings, so
        // matching the literal source text would let `where(name = '_sort')` through.
        var normalized = WhitespaceRun().Replace(expression, string.Empty);

        foreach (var resultParameter in SearchResultParameters)
        {
            if (normalized.Contains($"searchParam.where(name='{resultParameter}')", StringComparison.Ordinal))
                yield return $"gates on search result parameter '{resultParameter}'";
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    private static IEnumerable<string> EnumerateSuiteFiles()
    {
        var suitesRoot = Path.Combine(
            RepoRoot.Find(), "src", "Core", "Ignixa.TestScript.Suites", "testscripts");

        Directory.Exists(suitesRoot).ShouldBeTrue($"Expected conformance suites at {suitesRoot}.");
        return Directory.EnumerateFiles(suitesRoot, "*.json", SearchOption.AllDirectories);
    }

    private static IEnumerable<string> CollectIgnixaExtensionUrls(string filePath) =>
        CollectIgnixaExtensions(filePath).Select(extension => extension.Url);

    // Materialises to strings before the JsonDocument is disposed; JsonElement is only valid
    // for the lifetime of its document.
    private static List<(string Url, string? ValueString)> CollectIgnixaExtensions(string filePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(filePath));
        return CollectFromElement(document.RootElement).ToList();
    }

    // Recurses the whole document but only harvests url from members of an "extension" or
    // "modifierExtension" array. Two reasons it is not simply "every url property":
    //   - Resources carry their own canonical url (SearchParameter.url, ValueSet.url), and
    //     several fixtures mint those under this same host. Those are resource identities,
    //     not extensions, and flagging them is a false positive.
    //   - The search must still be depth-first over arbitrary nesting, because fhirfakes is
    //     declared on the inline resource body carried by fixture[].resource rather than at
    //     the top level, so a walk limited to TestScript.extension[]/test[].extension[]
    //     would miss it.
    private static IEnumerable<(string Url, string? ValueString)> CollectFromElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (IsExtensionArray(property))
                    {
                        foreach (var url in ReadIgnixaExtensions(property.Value))
                            yield return url;
                    }

                    foreach (var nested in CollectFromElement(property.Value))
                        yield return nested;
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in CollectFromElement(item))
                        yield return nested;
                }
                break;
        }
    }

    private static bool IsExtensionArray(JsonProperty property) =>
        property.Value.ValueKind == JsonValueKind.Array &&
        property.Name is "extension" or "modifierExtension";

    private static IEnumerable<(string Url, string? ValueString)> ReadIgnixaExtensions(JsonElement extensionArray)
    {
        foreach (var extension in extensionArray.EnumerateArray())
        {
            if (extension.ValueKind == JsonValueKind.Object &&
                extension.TryGetProperty("url", out var urlElement) &&
                urlElement.ValueKind == JsonValueKind.String &&
                urlElement.GetString() is { } url &&
                url.StartsWith(IgnixaExtensionPrefix, StringComparison.Ordinal))
            {
                var valueString = extension.TryGetProperty("valueString", out var valueElement) &&
                                  valueElement.ValueKind == JsonValueKind.String
                    ? valueElement.GetString()
                    : null;

                yield return (url, valueString);
            }
        }
    }
}

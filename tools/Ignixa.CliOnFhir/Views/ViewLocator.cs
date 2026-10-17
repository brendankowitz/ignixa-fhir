// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Views;

/// <summary>Finds the ViewDefinition a call names: inline JSON, a file, or a ViewDefinition in the folder by id or url.</summary>
internal static class ViewLocator
{
    /// <summary>The largest ViewDefinition file read; a view is a few kilobytes.</summary>
    public const int MaxFileBytes = 1024 * 1024;

    private const int MostNamed = 5;

    /// <exception cref="ToolRequestException">Nothing, or more than one thing, is named, or what is named is not a JSON object.</exception>
    public static LoadedView Resolve(FolderSession session, string view)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(view);

        string text = view.Trim();
        if (text.StartsWith('{'))
        {
            return new LoadedView(ParseObject(text, "The inline ViewDefinition"), LoadedView.InlineSource, FromFolder: false, Reference: null, Evidence: null);
        }

        if (Directory.Exists(text))
        {
            throw ToolRequestException.Error("invalid-view", $"'{QueryEcho.Of(text)}' is a directory; name a ViewDefinition file.");
        }

        return File.Exists(text) ? FromFile(session, text) : FromFolder(session, text);
    }

    private static LoadedView FromFile(FolderSession session, string path)
    {
        string text;
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes)
            {
                throw ToolRequestException.Error("invalid-view", $"'{QueryEcho.Of(path)}' is larger than {MaxFileBytes} bytes, which no ViewDefinition is.");
            }

            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw ToolRequestException.Error("view-unreadable", $"The ViewDefinition file '{QueryEcho.Of(path)}' could not be read: {ex.Message}");
        }

        // A file inside the data folder is folder data like any other, whatever way it was named.
        bool insideFolder = new FolderOverlap(session.RootPath).Reason(path, "view file") is not null;
        return new LoadedView(ParseObject(text, $"The ViewDefinition file '{QueryEcho.Of(path)}'"), LoadedView.FileSource, insideFolder, Reference: null, Evidence: null);
    }

    private static LoadedView FromFolder(FolderSession session, string key)
    {
        IReadOnlyList<FolderViewDefinition> all = FolderViewDefinitions.Find(session);
        List<FolderViewDefinition> matches = [.. all.Where(candidate => Matches(candidate, key))];
        if (matches.Count == 0)
        {
            string held = all.Count == 0
                ? "the folder holds no ViewDefinition resources"
                : $"the folder holds {all.Count} ViewDefinition resources: {string.Join(", ", all.Take(MostNamed).Select(candidate => candidate.Reference ?? candidate.Where))}{(all.Count > MostNamed ? ", ..." : string.Empty)}";
            throw ToolRequestException.Error("view-not-found", $"'{QueryEcho.Of(key)}' is not inline ViewDefinition JSON (text starting with '{{'), a file, or the id or url of a ViewDefinition in the folder ({held}).");
        }

        if (matches.Count > 1)
        {
            throw ToolRequestException.Error("view-ambiguous", $"'{QueryEcho.Of(key)}' names {matches.Count} ViewDefinitions in the folder, at {string.Join(", ", matches.Take(MostNamed).Select(candidate => candidate.Where))}; name one by its id, or give url|version.");
        }

        FolderViewDefinition match = matches[0];
        return new LoadedView(match.Json, LoadedView.FolderSource, FromFolder: true, match.Reference, match.Location);
    }

    // An id, ViewDefinition/id, a url (any version), or url|version.
    private static bool Matches(FolderViewDefinition candidate, string key)
    {
        if (candidate.Id is not null && (candidate.Id == key || $"{ViewDefinitions.ResourceType}/{candidate.Id}" == key))
        {
            return true;
        }

        int bar = key.IndexOf('|', StringComparison.Ordinal);
        string url = bar < 0 ? key : key[..bar];
        return candidate.Url is not null && candidate.Url == url && (bar < 0 || candidate.Version == key[(bar + 1)..]);
    }

    private static JsonObject ParseObject(string text, string what)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject
                ?? throw ToolRequestException.Error("invalid-view", $"{what} is not a JSON object.");
        }
        catch (JsonException ex)
        {
            throw ToolRequestException.Error("invalid-view", $"{what} is not valid JSON: {ex.Message}");
        }
    }
}

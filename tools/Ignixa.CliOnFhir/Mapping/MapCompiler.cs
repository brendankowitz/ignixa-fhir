// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Parser;

namespace Ignixa.CliOnFhir.Mapping;

/// <summary>
/// Parses a map with <c>Ignixa.FhirMappingLanguage</c>: FML with <see cref="MappingParser"/>, StructureMap JSON with
/// <see cref="StructureMapParser"/>. The limits of <see cref="MapGuards"/> are checked first on the text and then on the parsed map.
/// A problem is an <c>map-invalid</c> error located at the map's file (and line and column, for FML); the parser's own message is
/// repeated only for a map the caller gave, since it quotes the map's text, and a map of the folder is untrusted.
/// </summary>
internal static class MapCompiler
{
    private const string InvalidCode = "map-invalid";

    /// <exception cref="ToolRequestException"><c>map-invalid</c>.</exception>
    public static MapExpression Compile(LoadedMap loaded, FhirVersion version)
    {
        ArgumentNullException.ThrowIfNull(loaded);

        MapExpression map = loaded.Format == LoadedMap.Fml ? ParseFml(loaded) : ParseStructureMap(loaded, version);
        if (MapGuards.TreeViolation(map) is { } reason)
        {
            throw Invalid(loaded, $"{What(loaded)} {reason}.", line: null, column: null);
        }

        return map;
    }

    private static MapExpression ParseFml(LoadedMap loaded)
    {
        if (MapGuards.TextViolation(loaded.Text) is { } reason)
        {
            throw Invalid(loaded, $"{What(loaded)} {reason}, which the FML parser cannot take safely.", line: null, column: null);
        }

        try
        {
            return new MappingParser().Parse(loaded.Text);
        }
        catch (ParseException ex)
        {
            (int? line, int? column) = Position(ex);
            string at = line is int known ? $" at line {known}, column {column}" : string.Empty;
            throw Invalid(loaded, $"{What(loaded)} is not valid FML{at}{Detail(loaded, ex)}", line, column);
        }
        catch (ArgumentException)
        {
            throw Invalid(loaded, $"{What(loaded)} is empty.", line: null, column: null);
        }
    }

    private static MapExpression ParseStructureMap(LoadedMap loaded, FhirVersion version)
    {
        JsonObject json;
        try
        {
            json = JsonNode.Parse(loaded.Text) as JsonObject ?? throw Invalid(loaded, $"{What(loaded)} is not a JSON object.", line: null, column: null);
        }
        catch (JsonException ex)
        {
            int? line = ex.LineNumber is long known ? (int)known + 1 : null;
            int? column = ex.BytePositionInLine is long position ? (int)position + 1 : null;
            throw Invalid(loaded, $"{What(loaded)} is not valid JSON{(line is null ? string.Empty : $" at line {line}, byte {column}")}.", line, column);
        }

        if (json["resourceType"] is not JsonValue type || !type.TryGetValue(out string? resourceType) || resourceType != "StructureMap")
        {
            throw Invalid(loaded, $"{What(loaded)} is JSON but not a StructureMap (its resourceType must be StructureMap).", line: null, column: null);
        }

        try
        {
            return new StructureMapParser(version).Parse(new Ignixa.Models.StructureMap(json, version));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException or InvalidCastException or KeyNotFoundException or JsonException)
        {
            throw Invalid(loaded, $"{What(loaded)} could not be read as a StructureMap ({ex.GetType().Name}){Detail(loaded, ex)}", line: null, column: null);
        }
    }

    // Where the parser stopped. A failure of the tokenizer comes wrapped, at the start of the text, with the tokenizer's own
    // exception inside, which holds the real place; a start-of-text position with an inner exception and no place is unknown.
    private static (int? Line, int? Column) Position(ParseException ex)
    {
        if (ex.InnerException is Superpower.ParseException { ErrorPosition.HasValue: true } inner)
        {
            return (inner.ErrorPosition.Line, inner.ErrorPosition.Column);
        }

        bool wrapped = ex.InnerException is not null && ex.Position.Absolute == 0;
        return ex.Position.HasValue && !wrapped ? (ex.Position.Line, ex.Position.Column) : (null, null);
    }

    // The parser's message quotes the map; it is repeated only for a map the caller gave.
    private static string Detail(LoadedMap loaded, Exception ex) =>
        loaded.FromFolder ? $"; the parser's message is not repeated for a map of the folder (digest {FhirIds.Digest(ex.Message)})." : $": {QueryEcho.Of(ex.Message)}";

    private static string What(LoadedMap loaded) => loaded.Source switch
    {
        LoadedMap.FolderSource => $"The folder's {loaded.Reference}",
        LoadedMap.FileSource => $"The map file '{QueryEcho.Of(loaded.File!)}'",
        _ => "The inline map",
    };

    private static ToolRequestException Invalid(LoadedMap loaded, string diagnostics, int? line, int? column) =>
        new(new IssueItem("error", InvalidCode, diagnostics, loaded.FromFolder && loaded.Source == LoadedMap.FolderSource ? loaded.Where() : loaded.Where(line, column)));
}

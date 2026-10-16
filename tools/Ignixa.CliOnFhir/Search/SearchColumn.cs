// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Search;

/// <summary>One column of a search's table: a name and the FHIRPath evaluated on each matching resource.</summary>
/// <param name="Name">The column's name in <c>facts.columns</c>.</param>
/// <param name="Path">FHIRPath evaluated with the matching resource as its input, as a ViewDefinition's <c>column.path</c> is.</param>
public sealed record SearchColumn(string Name, string Path)
{
    /// <summary>The columns of a JSON object mapping names to FHIRPath, in the order written (the CLI's <c>--columns</c>).</summary>
    /// <exception cref="ToolRequestException">The text is not a JSON object of strings.</exception>
    public static IReadOnlyList<SearchColumn> FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        const string Shape = "a JSON object mapping each column name to a FHIRPath, for example {\"date\":\"authoredOn\",\"status\":\"status\"}";
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw ToolRequestException.Error("invalid-parameter", $"'columns' must be {Shape}.");
            }

            List<SearchColumn> columns = [];
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw ToolRequestException.Error("invalid-parameter", $"'columns' must be {Shape}; the value of '{property.Name}' is not a string.");
                }

                columns.Add(new SearchColumn(property.Name, property.Value.GetString()!));
            }

            return columns;
        }
        catch (JsonException ex)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'columns' must be {Shape}: {ex.Message}");
        }
    }

    /// <summary>The columns of a name-to-FHIRPath map, in its enumeration order (the MCP tool's <c>columns</c>).</summary>
    public static IReadOnlyList<SearchColumn> From(IEnumerable<KeyValuePair<string, string>> columns) =>
        [.. columns.Select(column => new SearchColumn(column.Key, column.Value))];
}

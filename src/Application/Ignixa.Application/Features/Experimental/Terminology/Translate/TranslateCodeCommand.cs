using System.Text.Json.Nodes;
using Medino;

namespace Ignixa.Application.Features.Experimental.Terminology.Translate;

/// <summary>
/// Command for ConceptMap $translate operation.
/// Translates code from one system to another using ConceptMap.
/// </summary>
public record TranslateCodeCommand(
    int TenantId,
    string Code,
    string System,
    string? Url = null,
    string? ConceptMapVersion = null,
    string? Version = null,
    string? Source = null,
    string? Target = null,
    string? TargetSystem = null,
    bool Reverse = false) : IRequest<TranslateCodeResult>;

/// <summary>
/// Result containing Parameters resource as JsonNode.
/// </summary>
public record TranslateCodeResult(JsonNode ParametersResource);

// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>
/// Builds a draft from its typed input: the resource of the type asked for, tagged as a draft (<see cref="TagSystem"/>
/// <c>draft</c>), with <c>meta.profile</c> when a profile is asked for, and an id that is the digest of the rest of it
/// (<c>draft-</c> and 32 hex digits of its SHA-256): the same input, profile and instant give the same id, and any change to the
/// resource gives another.
/// </summary>
internal static class DraftBuilder
{
    /// <summary>The system of the tag every draft carries.</summary>
    public const string TagSystem = "http://ignixa.dev/cli-on-fhir";

    /// <summary>The resource types a draft can be.</summary>
    public static IReadOnlyList<string> ResourceTypes { get; } = [MedicationRequestDraft.ResourceType, ServiceRequestDraft.ResourceType, ObservationDraft.ResourceType];

    /// <summary>The draft of <paramref name="resourceType"/> that <paramref name="input"/> describes.</summary>
    /// <exception cref="ToolRequestException"><see cref="DraftArguments.InvalidCode"/>: the input is not what the type's schema asks for.</exception>
    /// <param name="callMembers">Members of the caller's input that are the call's (see <see cref="DraftRequest.CallMembers"/>).</param>
    public static DraftContent Build(string resourceType, JsonObject input, string? profile, AsOf asOf, IReadOnlyList<string>? callMembers = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        var arguments = new DraftArguments(input, "input", callMembers);
        DraftContent content = resourceType switch
        {
            MedicationRequestDraft.ResourceType => MedicationRequestDraft.Build(arguments, asOf),
            ServiceRequestDraft.ResourceType => ServiceRequestDraft.Build(arguments, asOf),
            ObservationDraft.ResourceType => ObservationDraft.Build(arguments, asOf),
            _ => throw new ArgumentOutOfRangeException(nameof(resourceType), resourceType, "Not a draft resource type."),
        };

        JsonObject resource = content.Resource;
        resource.Insert(1, "meta", Meta(profile));
        resource.Insert(1, "id", Id(resource));
        return content;
    }

    private static JsonObject Meta(string? profile)
    {
        var meta = new JsonObject();
        if (profile is not null)
        {
            meta["profile"] = new JsonArray(JsonValue.Create(profile));
        }

        meta["tag"] = new JsonArray(new JsonObject { ["system"] = TagSystem, ["code"] = "draft" });
        return meta;
    }

    // The resource is built in a fixed member order from the input, so its compact JSON is the input normalized.
    private static string Id(JsonObject resource) =>
        "draft-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(resource.ToJsonString())).AsSpan(0, 16));
}

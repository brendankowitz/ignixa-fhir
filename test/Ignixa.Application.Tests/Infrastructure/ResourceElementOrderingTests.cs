// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Text.Json.Nodes;
using Ignixa.Application.Infrastructure;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure;

public class ResourceElementOrderingTests
{
    private const string UnorderedPatient = """{"active":true,"meta":{"profile":["http://example.org/p"]},"id":"p1","resourceType":"Patient"}""";

    private readonly R4CoreSchemaProvider _schema = new();

    [Fact]
    public void GivenOrderingDisabled_WhenOrderForStorage_ThenResourceIsUnchanged()
    {
        ResourceJsonNode patient = ResourceJsonNode.Parse(UnorderedPatient);

        CreateOrdering(enabled: false).OrderForStorage(patient, _schema);

        patient.SerializeToString().ShouldBe(UnorderedPatient);
    }

    [Fact]
    public void GivenOrderingEnabled_WhenStorageStampsMetaAfterOrdering_ThenStampedElementsStayInSpecOrder()
    {
        ResourceJsonNode patient = ResourceJsonNode.Parse(UnorderedPatient);

        CreateOrdering(enabled: true).OrderForStorage(patient, _schema);
        patient.Meta.VersionId = "7";
        patient.Meta.LastUpdatedOffset = DateTimeOffset.UtcNow;

        JsonObject stored = JsonNode.Parse(patient.SerializeToString())!.AsObject();
        stored.Select(property => property.Key).ShouldBe(["resourceType", "id", "meta", "active"]);
        stored["meta"]!.AsObject().Select(property => property.Key).ShouldBe(["versionId", "lastUpdated", "profile"]);
        stored["meta"]!["versionId"]!.GetValue<string>().ShouldBe("7");
    }

    private static ResourceElementOrdering CreateOrdering(bool enabled) =>
        new(Options.Create(new SerializationOptions { OrderElementsOnWrite = enabled }));
}

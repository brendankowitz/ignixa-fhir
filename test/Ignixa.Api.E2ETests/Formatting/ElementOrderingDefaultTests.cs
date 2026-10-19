// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Shouldly;

namespace Ignixa.Api.E2ETests.Formatting;

[Collection(E2ETestCollection.Name)]
public class ElementOrderingDefaultTests(IgnixaApiFixture fixture)
{
    [Fact]
    public async Task GivenDefaultSettings_WhenPuttingUnorderedResource_ThenReadKeepsClientOrder()
    {
        string id = Guid.NewGuid().ToString("N");
        using var content = new StringContent(
            $$"""{"birthDate":"1970-01-01","gender":"male","active":true,"id":"{{id}}","resourceType":"Patient"}""",
            Encoding.UTF8,
            "application/fhir+json");

        using var created = await fixture.Client.PutAsync($"/tenant/1/Patient/{id}", content);
        using var read = await fixture.Client.GetAsync($"/tenant/1/Patient/{id}");

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonObject resource = JsonNode.Parse(await read.Content.ReadAsStringAsync())!.AsObject();
        resource.Select(property => property.Key).Take(3).ShouldBe(["birthDate", "gender", "active"]);
    }
}

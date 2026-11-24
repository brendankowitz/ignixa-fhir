// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.Serialization.Tests;

public class JsonSourceNodeFactoryColdOptionsTests
{
    [Fact]
    public void GivenNoPriorParse_WhenSerializingWrappedClrValue_ThenSerializes()
    {
        var resource = new ResourceJsonNode(new JsonObject
        {
            ["resourceType"] = "Basic",
            ["tags"] = JsonValue.Create(new List<string> { "a" }),
        });

        resource.SerializeToString().ShouldBe("""{"resourceType":"Basic","tags":["a"]}""");
    }
}

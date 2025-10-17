// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.SourceNodeSerialization;

namespace Ignixa.Application.Tests.Features.Bundle.Serialization;

/// <summary>
/// Test JsonSourceNodeFactory to ensure it's available.
/// </summary>
public class JsonSourceNodeFactoryTest
{
    [Fact]
    public void CanParseJson()
    {
        var json = """{"resourceType":"Patient","id":"123"}""";
        var node = JsonSourceNodeFactory.Parse(json);
        Assert.NotNull(node);
        Assert.Equal("Patient", node.ResourceType);
    }
}

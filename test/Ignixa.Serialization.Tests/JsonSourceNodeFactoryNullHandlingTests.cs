// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.Serialization.Tests;

/// <summary>
/// Covers the case where <c>JsonSerializer.Deserialize</c> legitimately returns null (a bare JSON
/// <c>null</c> literal is valid JSON but not a resource) -- <see cref="JsonSourceNodeFactory.Parse{TResource}(string)"/>
/// and its overloads declare a non-nullable return type, so this must throw rather than propagate null.
/// </summary>
public class JsonSourceNodeFactoryNullHandlingTests
{
    [Fact]
    public void GivenJsonNullLiteral_WhenParsingFromString_ThenThrowsInvalidOperationException()
    {
        Should.Throw<InvalidOperationException>(() => JsonSourceNodeFactory.Parse<ResourceJsonNode>("null"));
    }

    [Fact]
    public void GivenJsonNullLiteral_WhenParsingFromBytes_ThenThrowsInvalidOperationException()
    {
        var bytes = Encoding.UTF8.GetBytes("null");

        Should.Throw<InvalidOperationException>(() => JsonSourceNodeFactory.Parse<ResourceJsonNode>(bytes));
    }

    [Fact]
    public async Task GivenJsonNullLiteral_WhenParsingAsyncFromStream_ThenThrowsInvalidOperationException()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("null"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => JsonSourceNodeFactory.ParseAsync<ResourceJsonNode>(stream, CancellationToken.None).AsTask());
    }
}

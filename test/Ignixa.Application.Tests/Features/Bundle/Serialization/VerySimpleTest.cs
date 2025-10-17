// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Ignixa.Application.Features.Bundle.Serialization;

namespace Ignixa.Application.Tests.Features.Bundle.Serialization;

/// <summary>
/// Very simple test to verify basic parsing.
/// </summary>
public class VerySimpleTest
{
    [Fact]
    public async Task CanParseAtAll()
    {
        // Arrange
        var logger = Substitute.For<ILogger<StreamingBundleParser>>();
        var parser = new StreamingBundleParser(logger);

        // Minimal bundle JSON
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "entry": [
            {
              "request": {
                "method": "GET",
                "url": "Patient/123"
              }
            }
          ]
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await parser.ParseStreamAsync(stream, CancellationToken.None);
        var count = 0;
        await foreach (var entry in context.Entries)
        {
            count++;
        }

        // Assert
        Assert.Equal(1, count);
    }
}

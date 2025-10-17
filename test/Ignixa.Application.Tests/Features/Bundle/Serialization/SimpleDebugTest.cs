// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Ignixa.Application.Features.Bundle.Serialization;
using Xunit.Abstractions;

namespace Ignixa.Application.Tests.Features.Bundle.Serialization;

/// <summary>
/// Simple debug test to see what the parser is producing.
/// </summary>
public class SimpleDebugTest
{
    private readonly ITestOutputHelper _output;

    public SimpleDebugTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Debug_SimpleBundle()
    {
        // Arrange
        var logger = Substitute.For<ILogger<StreamingBundleParser>>();
        var parser = new StreamingBundleParser(logger);

        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "transaction",
          "entry": [
            {
              "resource": {
                "resourceType": "Patient",
                "id": "example-123"
              },
              "request": {
                "method": "PUT",
                "url": "Patient/example-123"
              }
            }
          ]
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await parser.ParseStreamAsync(stream, CancellationToken.None);

        _output.WriteLine($"Bundle ResourceType: {context.ResourceType}");
        _output.WriteLine($"Bundle Type: {context.BundleType}");
        _output.WriteLine($"Links: {context.Links.Count}");
        _output.WriteLine($"Parsing Issues: {context.ParsingIssues.Count}");

        var entries = new List<Ignixa.Application.Features.Bundle.BundleEntryContext>();
        await foreach (var entry in context.Entries)
        {
            _output.WriteLine($"Entry Index: {entry.Index}");
            _output.WriteLine($"ResourceType: {entry.ResourceType}");
            _output.WriteLine($"ResourceId: {entry.ResourceId}");
            _output.WriteLine($"HttpVerb: {entry.HttpVerb}");
            _output.WriteLine($"RequestUrl: {entry.RequestUrl}");
            _output.WriteLine($"RawJson: {entry.RawJson}");
            entries.Add(entry);
        }

        // Assert
        _output.WriteLine($"Total entries: {entries.Count}");
        Assert.Single(entries);
    }
}

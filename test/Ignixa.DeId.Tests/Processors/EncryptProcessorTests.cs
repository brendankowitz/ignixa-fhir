// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text;
using Ignixa.DeId.Models;
using Ignixa.DeId.Processors;
using Ignixa.DeId.Tools;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;

namespace Ignixa.DeId.Tests.Processors;

public class EncryptProcessorTests
{
    private readonly R4CoreSchemaProvider _schema = new();

    [Theory]
    [InlineData("1234567890123456")]
    [InlineData("123456789012345678901234")]
    [InlineData("12345678901234567890123456789012")]
    [InlineData("\u00e9\u00e9\u00e9\u00e9\u00e9\u00e9\u00e9\u00e9")]
    public async Task GivenValidUtf8Key_WhenEncrypting_ThenValueAndProcessedPathAreReturned(string key)
    {
        var resource = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"original"}""");
        var node = resource.ToElement(_schema).Children("id").First();
        var processor = new EncryptProcessor(key);
        var context = new ProcessorContext { ResourceId = "original", VisitedNodes = [] };

        var result = await processor.ProcessAsync(resource, node, context, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.WasModified.ShouldBeTrue();
        result.Value.OperationType.ShouldBe(DeIdOperations.Encrypt);
        result.Value.ProcessedPaths.ShouldBe([node.Location]);
        resource.InvalidateCaches();
        var encrypted = resource.ToElement(_schema).Children("id").First().Value!.ToString();
        encrypted.ShouldNotBe("original");
        EncryptTool.DecryptTextFromBase64WithAes(encrypted, Encoding.UTF8.GetBytes(key)).ShouldBe("original");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("123456789012345")]
    [InlineData("12345678901234567")]
    [InlineData("12345678901234567890123")]
    [InlineData("1234567890123456789012345")]
    [InlineData("1234567890123456789012345678901")]
    [InlineData("123456789012345678901234567890123")]
    public void GivenInvalidKeyLength_WhenCreatingProcessor_ThenKeyIsRejected(string key)
    {
        Should.Throw<ArgumentException>(() => new EncryptProcessor(key));
    }

    [Fact]
    public async Task GivenComplexElement_WhenEncrypting_ThenNoModificationIsReported()
    {
        var resource = ResourceJsonNode.Parse("""{"resourceType":"Patient","name":[{"family":"original"}]}""");
        var node = resource.ToElement(_schema).Children("name").First();
        var processor = new EncryptProcessor("1234567890123456");
        var context = new ProcessorContext { ResourceId = "patient", VisitedNodes = [] };

        var result = await processor.ProcessAsync(resource, node, context, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.WasModified.ShouldBeFalse();
        result.Value.ProcessedPaths.ShouldBeEmpty();
        resource.InvalidateCaches();
        resource.ToElement(_schema).Children("name").First().Children("family").First().Value.ShouldBe("original");
    }
}

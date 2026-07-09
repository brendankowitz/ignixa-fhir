// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Ignixa.Serialization.TestSupport;

namespace Ignixa.FhirFakes.Tests;

public class ResourceBundleComposerTests
{
    [Fact]
    public void GivenNullElementInSequence_WhenComposingTransactionBundle_ThenThrowsArgumentException()
    {
        var valid = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"abc"}""");
        ResourceJsonNode[] resources = [valid, null!];

        var exception = Should.Throw<ArgumentException>(() => ResourceBundleComposer.ToTransactionBundle(resources));
        exception.Message.ShouldContain("index 1");
    }

    [Fact]
    public void GivenNullElementInSequence_WhenComposingBatchBundle_ThenThrowsArgumentException()
    {
        var valid = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"abc"}""");
        ResourceJsonNode[] resources = [valid, null!];

        var exception = Should.Throw<ArgumentException>(() => ResourceBundleComposer.ToBatchBundle(resources));
        exception.Message.ShouldContain("index 1");
    }

    [Fact]
    public void GivenResourceWithMissingId_WhenComposingTransactionBundle_ThenThrowsArgumentExceptionIdentifyingResourceType()
    {
        var resource = ResourceJsonNode.Parse("""{"resourceType":"Patient"}""");

        var exception = Should.Throw<ArgumentException>(() => ResourceBundleComposer.ToTransactionBundle([resource]));
        exception.Message.ShouldContain("Patient");
        exception.Message.ShouldContain("no id");
    }

    [Fact]
    public void GivenResourceWithMissingResourceType_WhenComposingTransactionBundle_ThenThrowsArgumentException()
    {
        var resource = ResourceJsonNode.Parse("""{"id":"abc"}""");

        var exception = Should.Throw<ArgumentException>(() => ResourceBundleComposer.ToTransactionBundle([resource]));
        exception.Message.ShouldContain("no resourceType");
    }

    [Fact]
    public void GivenResourceWithMissingId_WhenComposingBatchBundle_ThenThrowsArgumentException()
    {
        var resource = ResourceJsonNode.Parse("""{"resourceType":"Encounter"}""");

        var exception = Should.Throw<ArgumentException>(() => ResourceBundleComposer.ToBatchBundle([resource]));
        exception.Message.ShouldContain("Encounter");
        exception.Message.ShouldContain("no id");
    }

    [Fact]
    public void GivenValidResources_WhenComposingTransactionBundle_ThenSucceeds()
    {
        var resource = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"abc"}""");

        var bundle = ResourceBundleComposer.ToTransactionBundle([resource]);

        bundle.MutableNode()["type"]?.GetValue<string>().ShouldBe("transaction");
    }
}

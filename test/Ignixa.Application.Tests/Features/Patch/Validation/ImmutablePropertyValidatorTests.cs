// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Ignixa.Application.Features.Patch;
using Ignixa.Application.Features.Patch.Validation;
using Ignixa.Serialization.SourceNodes;
using Xunit;

namespace Ignixa.Application.Tests.Features.Patch.Validation;

public class ImmutablePropertyValidatorTests
{
    private readonly ImmutablePropertyValidator _validator;

    public ImmutablePropertyValidatorTests()
    {
        _validator = new ImmutablePropertyValidator();
    }

    [Fact]
    public void GivenUnchangedId_WhenValidating_ThenNoExceptionThrown()
    {
        var before = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        var after = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };

        _validator.Validate(before, after);
    }

    [Fact]
    public void GivenChangedId_WhenValidating_ThenThrowsException()
    {
        var before = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        var after = new ResourceJsonNode { ResourceType = "Patient", Id = "456" };

        var ex = Assert.Throws<FhirPatchException>(() => _validator.Validate(before, after));
        Assert.Contains("id", ex.Message);
        Assert.Contains("immutable", ex.Message);
    }

    [Fact]
    public void GivenUnchangedVersionId_WhenValidating_ThenNoExceptionThrown()
    {
        var before = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        before.Meta.VersionId = "1";

        var after = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        after.Meta.VersionId = "1";

        _validator.Validate(before, after);
    }

    [Fact]
    public void GivenChangedVersionId_WhenValidating_ThenThrowsException()
    {
        var before = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        before.Meta.VersionId = "1";

        var after = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        after.Meta.VersionId = "2";

        var ex = Assert.Throws<FhirPatchException>(() => _validator.Validate(before, after));
        Assert.Contains("meta.versionId", ex.Message);
        Assert.Contains("immutable", ex.Message);
    }

    [Fact]
    public void GivenUnchangedLastUpdated_WhenValidating_ThenNoExceptionThrown()
    {
        var timestamp = DateTimeOffset.UtcNow;

        var before = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        before.Meta.LastUpdated = timestamp;

        var after = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        after.Meta.LastUpdated = timestamp;

        _validator.Validate(before, after);
    }

    [Fact]
    public void GivenChangedLastUpdated_WhenValidating_ThenThrowsException()
    {
        var before = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        before.Meta.LastUpdated = DateTimeOffset.UtcNow.AddMinutes(-10);

        var after = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        after.Meta.LastUpdated = DateTimeOffset.UtcNow;

        var ex = Assert.Throws<FhirPatchException>(() => _validator.Validate(before, after));
        Assert.Contains("meta.lastUpdated", ex.Message);
        Assert.Contains("immutable", ex.Message);
    }

    [Fact]
    public void GivenNullVersionIdInBefore_WhenValidating_ThenNoExceptionThrown()
    {
        var before = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        var after = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        after.Meta.VersionId = "1";

        _validator.Validate(before, after);
    }

    [Fact]
    public void GivenNullVersionIdInAfter_WhenValidating_ThenNoExceptionThrown()
    {
        var before = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        before.Meta.VersionId = "1";

        var after = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };

        _validator.Validate(before, after);
    }

    [Fact]
    public void GivenNullLastUpdatedInBefore_WhenValidating_ThenNoExceptionThrown()
    {
        var before = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        var after = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        after.Meta.LastUpdated = DateTimeOffset.UtcNow;

        _validator.Validate(before, after);
    }

    [Fact]
    public void GivenNullLastUpdatedInAfter_WhenValidating_ThenNoExceptionThrown()
    {
        var before = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        before.Meta.LastUpdated = DateTimeOffset.UtcNow;

        var after = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };

        _validator.Validate(before, after);
    }

    [Fact]
    public void GivenMinimalTimeDifference_WhenValidatingLastUpdated_ThenNoExceptionThrown()
    {
        var timestamp = DateTimeOffset.UtcNow;

        var before = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        before.Meta.LastUpdated = timestamp;

        var after = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        after.Meta.LastUpdated = timestamp.AddMilliseconds(500); // Within 1 second

        _validator.Validate(before, after);
    }
}

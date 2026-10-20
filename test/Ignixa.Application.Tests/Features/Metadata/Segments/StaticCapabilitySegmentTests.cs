// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.Metadata.Segments;

public class StaticCapabilitySegmentTests
{
    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    public async Task GivenSupportedVersion_WhenApplyingStaticSegment_ThenOnlyFhirPathPatchFormatIsAdvertised(FhirVersion version)
    {
        // Arrange
        var versionInfo = Substitute.For<IApplicationVersionInfo>();
        versionInfo.Version.Returns("1.0.0");
        versionInfo.Name.Returns("Ignixa");
        versionInfo.ReleaseDate.Returns("2026-01-01");
        var segment = new StaticCapabilitySegment(versionInfo, NullLogger<StaticCapabilitySegment>.Instance);
        var statement = new CapabilityStatementJsonNode();

        // Act
        await segment.ApplyAsync(statement, new CapabilityContext(version, TenantId: 1), CancellationToken.None);

        // Assert
        statement.PatchFormat.ToList().ShouldBe(["application/fhir+json"]);
    }
}

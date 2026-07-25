// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlEntityFramework.Compression;
using Ignixa.DataLayer.SqlEntityFramework.RowGenerators;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.IO;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests.RowGenerators;

/// <summary>
/// Pins <see cref="ResourceRowGenerator"/>'s behavior when a resource's type is absent from
/// <c>dbo.ResourceType</c>: it must throw <see cref="InternalServerErrorException"/> (a server-side
/// reference-data fault, HTTP 500) rather than silently dropping the resource from the ResourceList TVP.
/// </summary>
public class ResourceRowGeneratorTests
{
    [Fact]
    public void GivenAResourceTypeAbsentFromTheMap_WhenGeneratingRows_ThenThrowsRatherThanSilentlyDropping()
    {
        // Arrange
        var compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());
        var generator = new ResourceRowGenerator(compressor);

        var wrapper = new ResourceWrapper(
            ResourceType: "Measure",
            ResourceId: "m1",
            VersionId: "1",
            LastModified: DateTimeOffset.UtcNow,
            Resource: new ResourceJsonNode { ResourceType = "Measure", Id = "m1" },
            Request: new ResourceRequest("POST", "Measure"));

        var resourceTypeIdMap = new Dictionary<string, short>(); // "Measure" deliberately absent

        // Act & Assert: GenerateSqlDataRecords is a yield iterator, so the throw is deferred until
        // enumeration -- materializing with ToList() is load-bearing here.
        var ex = Should.Throw<InternalServerErrorException>(() =>
            generator.GenerateSqlDataRecords(
                transactionId: 1L,
                resources: [wrapper],
                resourceTypeIdMap: resourceTypeIdMap,
                entryIndices: [0]).ToList());

        ex.Message.ShouldContain("dbo.ResourceType");
    }
}

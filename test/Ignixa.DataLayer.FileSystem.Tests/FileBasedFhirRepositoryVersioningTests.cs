// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.DataLayer.FileSystem.FileSystem;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.DataLayer.FileSystem.Tests;

/// <summary>
/// Regression coverage for the stored-resource-bytes vs. sidecar-metadata versionId mismatch:
/// <see cref="FileBasedFhirRepository"/> must stamp the resolved version into the resource's own
/// <c>meta.versionId</c> before serializing it, not only into the metadata sidecar (see
/// SqlEntityFrameworkRepository.CreateOrUpdateAsync for the equivalent SQL-layer behavior).
/// </summary>
public sealed class FileBasedFhirRepositoryVersioningTests : IDisposable
{
    private readonly string _baseDirectory;
    private readonly FileBasedFhirRepository _repository;

    public FileBasedFhirRepositoryVersioningTests()
    {
        _baseDirectory = Path.Combine(Path.GetTempPath(), $"ignixa-filerepo-tests-{Guid.NewGuid()}");
        _repository = new FileBasedFhirRepository(_baseDirectory, NullLogger<FileBasedFhirRepository>.Instance);
    }

    [Fact]
    public async Task GivenResourceUpdatedTwice_WhenReadingCurrentVersion_ThenStoredResourceMetaVersionIdIsTwo()
    {
        // Arrange
        var key = new ResourceKey("Patient", "p1");
        await _repository.CreateOrUpdateAsync(CreateWrapper("Patient", "p1"));

        // Act
        await _repository.CreateOrUpdateAsync(CreateWrapper("Patient", "p1"));
        SearchEntryResult? current = await _repository.GetAsync(key);

        // Assert
        current.ShouldNotBeNull();
        current.VersionId.ShouldBe("2");
        ExtractVersionIdFromBytes(current.ResourceBytes).ShouldBe("2");
    }

    [Fact]
    public async Task GivenResourceWithMultipleVersions_WhenReadingHistory_ThenEachEntryResourceBytesVersionIdMatchesSearchEntryResultVersionId()
    {
        // Arrange
        var key = new ResourceKey("Patient", "p2");
        await _repository.CreateOrUpdateAsync(CreateWrapper("Patient", "p2"));
        await _repository.CreateOrUpdateAsync(CreateWrapper("Patient", "p2"));
        await _repository.CreateOrUpdateAsync(CreateWrapper("Patient", "p2"));

        // Act
        var history = new List<SearchEntryResult>();
        await foreach (SearchEntryResult entry in _repository.GetResourceHistoryAsync(key, new HistoryQueryParameters()))
        {
            history.Add(entry);
        }

        // Assert
        history.Count.ShouldBe(3);
        foreach (SearchEntryResult entry in history)
        {
            // This is the property ReferenceIndex relies on when resolving versioned references
            // (e.g. Patient/p2/_history/2): the bytes it parses must agree with the version the
            // repository claims via SearchEntryResult.VersionId, not just the sidecar metadata.
            ExtractVersionIdFromBytes(entry.ResourceBytes).ShouldBe(entry.VersionId);
        }
    }

    [Fact]
    public async Task GivenResourceCreatedAndUpdatedViaBatchWriteInSeparateTransactions_WhenReadingHistory_ThenBothVersionsPersistWithMatchingContentAndVersionIds()
    {
        // Arrange
        var key = new ResourceKey("Patient", "p3");
        var createOperations = new List<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)>
        {
            ("Patient", "p3", CreateNode("Patient", "p3", "male"), Array.Empty<object>(), "POST", 0)
        };
        var updateOperations = new List<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)>
        {
            ("Patient", "p3", CreateNode("Patient", "p3", "female"), Array.Empty<object>(), "PUT", 0)
        };

        // Act - two separate batch transactions: one create, one update
        IReadOnlyList<ResourceKey> createResults = await _repository.BatchWriteAsync(new TransactionId(1), createOperations);
        IReadOnlyList<ResourceKey> updateResults = await _repository.BatchWriteAsync(new TransactionId(2), updateOperations);

        // Assert - BatchWriteAsync reports the versions it resolved
        createResults.Count.ShouldBe(1);
        updateResults.Count.ShouldBe(1);
        createResults[0].VersionId.ShouldBe("1");
        updateResults[0].VersionId.ShouldBe("2");

        // Assert - the current read reflects the update, and the stored bytes actually contain
        // the resource (not "{}"): both the distinctive "gender" field and meta.versionId must
        // round-trip through WriteResourceFileAsync's serialization.
        SearchEntryResult? current = await _repository.GetAsync(key);
        current.ShouldNotBeNull();
        current.VersionId.ShouldBe("2");
        ExtractVersionIdFromBytes(current.ResourceBytes).ShouldBe("2");
        ExtractGenderFromBytes(current.ResourceBytes).ShouldBe("female");

        // Act - read full history
        var history = new List<SearchEntryResult>();
        await foreach (SearchEntryResult entry in _repository.GetResourceHistoryAsync(key, new HistoryQueryParameters()))
        {
            history.Add(entry);
        }

        // Assert - both versions must be present. Today, v1 silently disappears from history:
        // its stored bytes are "{}", so ReadResourceFromNdjsonByIdAsync can't find "p3" inside
        // them, LoadResourceVersionAsync catches the failure and returns null, and the version is
        // dropped without any test-visible error.
        history.Count.ShouldBe(2);
        SearchEntryResult? v1 = null;
        SearchEntryResult? v2 = null;
        foreach (SearchEntryResult entry in history)
        {
            if (entry.VersionId == "1")
            {
                v1 = entry;
            }
            else if (entry.VersionId == "2")
            {
                v2 = entry;
            }
        }

        v1.ShouldNotBeNull();
        v2.ShouldNotBeNull();
        ExtractVersionIdFromBytes(v1.ResourceBytes).ShouldBe("1");
        ExtractVersionIdFromBytes(v2.ResourceBytes).ShouldBe("2");
        ExtractGenderFromBytes(v1.ResourceBytes).ShouldBe("male");
        ExtractGenderFromBytes(v2.ResourceBytes).ShouldBe("female");
    }

    [Fact]
    public async Task GivenTwoBatchWritesShareOneTransaction_WhenReadingBothResourcesAndFile_ThenBothPersistAndNoBomIsEmbedded()
    {
        // Arrange - two BatchWriteAsync calls sharing the SAME transaction id, for distinct
        // resource ids of the same resource type. This is the case the separate-transaction
        // test above does not cover: the second call appends onto the NDJSON file the first
        // call already created for this resource type/day/transaction, rather than each call
        // creating its own file.
        var transactionId = new TransactionId(100);
        var key1 = new ResourceKey("Patient", "batch-bom-1");
        var key2 = new ResourceKey("Patient", "batch-bom-2");

        var firstBatch = new List<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)>
        {
            ("Patient", "batch-bom-1", CreateNode("Patient", "batch-bom-1", "male"), Array.Empty<object>(), "POST", 0)
        };
        var secondBatch = new List<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)>
        {
            ("Patient", "batch-bom-2", CreateNode("Patient", "batch-bom-2", "female"), Array.Empty<object>(), "POST", 0)
        };

        // Act
        await _repository.BatchWriteAsync(transactionId, firstBatch);
        await _repository.BatchWriteAsync(transactionId, secondBatch);

        // Assert - both resources are individually readable via GetAsync with correct content.
        // Before the fix, WriteResourceFileAsync's second (appending) call wrote a UTF-8 BOM
        // preamble mid-file, right before batch-bom-2's line, corrupting its JSON.
        SearchEntryResult? current1 = await _repository.GetAsync(key1);
        SearchEntryResult? current2 = await _repository.GetAsync(key2);
        current1.ShouldNotBeNull();
        current2.ShouldNotBeNull();
        ExtractGenderFromBytes(current1.ResourceBytes).ShouldBe("male");
        ExtractGenderFromBytes(current2.ResourceBytes).ShouldBe("female");

        // Assert - both appear in history. Before the fix, batch-bom-2 fails to parse,
        // LoadResourceVersionAsync swallows the exception and returns null, and the resource
        // silently vanishes from history even though BatchWriteAsync reported success.
        var history1 = new List<SearchEntryResult>();
        await foreach (SearchEntryResult entry in _repository.GetResourceHistoryAsync(key1, new HistoryQueryParameters()))
        {
            history1.Add(entry);
        }

        var history2 = new List<SearchEntryResult>();
        await foreach (SearchEntryResult entry in _repository.GetResourceHistoryAsync(key2, new HistoryQueryParameters()))
        {
            history2.Add(entry);
        }

        history1.Count.ShouldBe(1);
        history2.Count.ShouldBe(1);

        // Assert - the physical NDJSON file both BatchWriteAsync calls wrote/appended to
        // contains no UTF-8 BOM anywhere. The fix removes the preamble entirely (both the
        // create and append paths use the same BOM-less encoding), so the intended shape is
        // zero BOM occurrences in the file - not merely "none after the first line".
        string[] matchingFiles = Directory.GetFiles(_baseDirectory, $"tx-{transactionId}.ndjson", SearchOption.AllDirectories);
        matchingFiles.Length.ShouldBe(1);

        byte[] fileBytes = await File.ReadAllBytesAsync(matchingFiles[0]);
        CountUtf8BomOccurrences(fileBytes).ShouldBe(0);

        // Sanity: exactly two physical NDJSON lines were written (both resources are present in
        // the raw file, not just recoverable via some other path).
        string ndjsonContent = System.Text.Encoding.UTF8.GetString(fileBytes);
        ndjsonContent.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(2);
    }

    private static int CountUtf8BomOccurrences(byte[] bytes)
    {
        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
        int count = 0;
        for (int i = 0; i + bom.Length <= bytes.Length; i++)
        {
            if (bytes.AsSpan(i, bom.Length).SequenceEqual(bom))
            {
                count++;
            }
        }

        return count;
    }

    private static ResourceWrapper CreateWrapper(string resourceType, string resourceId)
    {
        var node = ResourceJsonNode.Parse($$"""{"resourceType":"{{resourceType}}","id":"{{resourceId}}"}""");

        return new ResourceWrapper(
            ResourceType: resourceType,
            ResourceId: resourceId,
            VersionId: "1",
            LastModified: DateTimeOffset.UtcNow,
            Resource: node,
            Request: new ResourceRequest("PUT", $"{resourceType}/{resourceId}"));
    }

    private static ResourceJsonNode CreateNode(string resourceType, string resourceId, string gender)
    {
        return ResourceJsonNode.Parse($$"""{"resourceType":"{{resourceType}}","id":"{{resourceId}}","gender":"{{gender}}"}""");
    }

    private static string ExtractVersionIdFromBytes(ReadOnlyMemory<byte> resourceBytes)
    {
        using JsonDocument document = JsonDocument.Parse(resourceBytes);
        return document.RootElement.GetProperty("meta").GetProperty("versionId").GetString()!;
    }

    private static string ExtractGenderFromBytes(ReadOnlyMemory<byte> resourceBytes)
    {
        using JsonDocument document = JsonDocument.Parse(resourceBytes);
        return document.RootElement.GetProperty("gender").GetString()!;
    }

    public void Dispose()
    {
        _repository.Dispose();

        if (Directory.Exists(_baseDirectory))
        {
            Directory.Delete(_baseDirectory, recursive: true);
        }
    }
}

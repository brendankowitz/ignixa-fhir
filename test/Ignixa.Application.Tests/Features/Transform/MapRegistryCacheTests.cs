// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Application.Operations.Features.Transform;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.FhirMappingLanguage.Parser;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Ignixa.Application.Operations.Tests.Features.Transform;

/// <summary>
/// Unit tests for MapRegistryCache.
/// Tests caching behavior, invalidation, statistics, and performance.
/// </summary>
public class MapRegistryCacheTests
{
    private readonly IPackageResourceRepository _mockRepository;
    private readonly StructureMapParser _parser;
    private readonly MapRegistryCache _cache;

    private const string TestMapUrl = "http://example.org/StructureMap/TestMap";
    private const string TestPackageId = "test.package";
    private const string TestPackageVersion = "1.0.0";

    public MapRegistryCacheTests()
    {
        _mockRepository = Substitute.For<IPackageResourceRepository>();
        _parser = new StructureMapParser();
        _cache = new MapRegistryCache(
            _mockRepository,
            _parser,
            NullLogger<MapRegistryCache>.Instance);
    }

    #region GetOrLoadAsync Tests

    [Fact]
    public async Task GivenMapNotCached_WhenGetOrLoadAsync_ThenLoadsFromRepository()
    {
        // Arrange
        var packageResource = CreateTestPackageResource();
        _mockRepository.GetStructureMapByUrlAsync(TestMapUrl, Arg.Any<CancellationToken>())
            .Returns(packageResource);

        // Act
        var result = await _cache.GetOrLoadAsync(TestMapUrl);

        // Assert
        result.Should().NotBeNull();
        result.Url.Should().Be(TestMapUrl);

        await _mockRepository.Received(1).GetStructureMapByUrlAsync(
            TestMapUrl,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenMapCached_WhenGetOrLoadAsync_ThenReturnsCachedMap()
    {
        // Arrange
        var packageResource = CreateTestPackageResource();
        _mockRepository.GetStructureMapByUrlAsync(TestMapUrl, Arg.Any<CancellationToken>())
            .Returns(packageResource);

        // First call - loads from repository
        await _cache.GetOrLoadAsync(TestMapUrl);

        // Act - Second call should use cache
        var result = await _cache.GetOrLoadAsync(TestMapUrl);

        // Assert
        result.Should().NotBeNull();
        result.Url.Should().Be(TestMapUrl);

        // Repository should only be called once (first call)
        await _mockRepository.Received(1).GetStructureMapByUrlAsync(
            TestMapUrl,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenMapNotFound_WhenGetOrLoadAsync_ThenThrowsException()
    {
        // Arrange
        _mockRepository.GetStructureMapByUrlAsync(TestMapUrl, Arg.Any<CancellationToken>())
            .Returns((PackageResource)null);

        // Act & Assert
        var act = async () => await _cache.GetOrLoadAsync(TestMapUrl);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"StructureMap not found: {TestMapUrl}");
    }

    #endregion

    #region Register Tests

    [Fact]
    public void GivenInlineMap_WhenRegister_ThenCachesMap()
    {
        // Arrange
        var map = CreateTestMapExpression();

        // Act
        _cache.Register(map);

        // Assert
        _cache.Contains(map.Url).Should().BeTrue();
        var retrieved = _cache.GetByUrl(map.Url);
        retrieved.Should().NotBeNull();
        retrieved!.Url.Should().Be(map.Url);
    }

    [Fact]
    public void GivenMapAlreadyExists_WhenRegister_ThenUpdatesMap()
    {
        // Arrange
        var map1 = CreateTestMapExpression();
        var map2 = CreateTestMapExpression(); // Same URL, different instance

        _cache.Register(map1);

        // Act
        _cache.Register(map2);

        // Assert
        _cache.Contains(map1.Url).Should().BeTrue();
        var stats = _cache.GetStatistics();
        stats.CachedMapCount.Should().Be(1);
    }

    #endregion

    #region Invalidation Tests

    [Fact]
    public async Task GivenCachedMapsFromPackage_WhenInvalidatePackage_ThenRemovesMaps()
    {
        // Arrange
        var packageResource = CreateTestPackageResource();
        _mockRepository.GetStructureMapByUrlAsync(TestMapUrl, Arg.Any<CancellationToken>())
            .Returns(packageResource);

        await _cache.GetOrLoadAsync(TestMapUrl);

        _cache.Contains(TestMapUrl).Should().BeTrue();

        // Act
        _cache.InvalidatePackage(TestPackageId);

        // Assert
        _cache.Contains(TestMapUrl).Should().BeFalse();
        _cache.GetStatistics().CachedMapCount.Should().Be(0);
    }

    [Fact]
    public async Task GivenCachedMapsFromMultiplePackages_WhenInvalidateOnePackage_ThenOnlyRemovesThatPackage()
    {
        // Arrange
        var packageResource1 = CreateTestPackageResource("http://example.org/Map1", "package.a", "1.0.0");
        var packageResource2 = CreateTestPackageResource("http://example.org/Map2", "package.b", "1.0.0");

        _mockRepository.GetStructureMapByUrlAsync("http://example.org/Map1", Arg.Any<CancellationToken>())
            .Returns(packageResource1);
        _mockRepository.GetStructureMapByUrlAsync("http://example.org/Map2", Arg.Any<CancellationToken>())
            .Returns(packageResource2);

        await _cache.GetOrLoadAsync("http://example.org/Map1");
        await _cache.GetOrLoadAsync("http://example.org/Map2");

        // Act
        _cache.InvalidatePackage("package.a");

        // Assert
        _cache.Contains("http://example.org/Map1").Should().BeFalse();
        _cache.Contains("http://example.org/Map2").Should().BeTrue();
    }

    [Fact]
    public void GivenInlineMaps_WhenInvalidatePackage_ThenDoesNotRemoveInlineMaps()
    {
        // Arrange
        var inlineMap = CreateTestMapExpression();
        _cache.Register(inlineMap);

        // Act
        _cache.InvalidatePackage(TestPackageId);

        // Assert
        _cache.Contains(inlineMap.Url).Should().BeTrue();
    }

    #endregion

    #region Statistics Tests

    [Fact]
    public async Task GivenCacheMisses_WhenGetStatistics_ThenReflectsMisses()
    {
        // Arrange
        var packageResource = CreateTestPackageResource();
        _mockRepository.GetStructureMapByUrlAsync(TestMapUrl, Arg.Any<CancellationToken>())
            .Returns(packageResource);

        // Act
        await _cache.GetOrLoadAsync(TestMapUrl);

        // Assert
        var stats = _cache.GetStatistics();
        stats.CacheMisses.Should().Be(1);
        stats.CacheHits.Should().Be(0);
        stats.TotalRequests.Should().Be(1);
        stats.HitRate.Should().Be(0.0);
    }

    [Fact]
    public async Task GivenCacheHits_WhenGetStatistics_ThenReflectsHits()
    {
        // Arrange
        var packageResource = CreateTestPackageResource();
        _mockRepository.GetStructureMapByUrlAsync(TestMapUrl, Arg.Any<CancellationToken>())
            .Returns(packageResource);

        await _cache.GetOrLoadAsync(TestMapUrl); // Miss

        // Act
        await _cache.GetOrLoadAsync(TestMapUrl); // Hit
        await _cache.GetOrLoadAsync(TestMapUrl); // Hit

        // Assert
        var stats = _cache.GetStatistics();
        stats.CacheMisses.Should().Be(1);
        stats.CacheHits.Should().Be(2);
        stats.TotalRequests.Should().Be(3);
        stats.HitRate.Should().BeApproximately(0.667, 0.01); // 2/3
    }

    [Fact]
    public void GivenStatistics_WhenResetStatistics_ThenClearsCounters()
    {
        // Arrange
        var map = CreateTestMapExpression();
        _cache.Register(map);
        _cache.GetByUrl(map.Url); // Hit

        _cache.GetStatistics().CacheHits.Should().BeGreaterThan(0);

        // Act
        _cache.ResetStatistics();

        // Assert
        var stats = _cache.GetStatistics();
        stats.CacheHits.Should().Be(0);
        stats.CacheMisses.Should().Be(0);
        stats.TotalRequests.Should().Be(0);
    }

    #endregion

    #region TTL Tests

    [Fact]
    public void GivenExpiredMap_WhenGetByUrl_ThenReturnsNull()
    {
        // Arrange
        _cache.TimeToLive = TimeSpan.FromMilliseconds(1);
        var map = CreateTestMapExpression();
        _cache.Register(map);

        // Wait for expiration
        Thread.Sleep(50);

        // Act
        var result = _cache.GetByUrl(map.Url);

        // Assert
        result.Should().BeNull();
        _cache.Contains(map.Url).Should().BeFalse();
    }

    #endregion

    #region Clear Tests

    [Fact]
    public void GivenCachedMaps_WhenClear_ThenRemovesAllMaps()
    {
        // Arrange
        var map1 = CreateTestMapExpression("http://example.org/Map1");
        var map2 = CreateTestMapExpression("http://example.org/Map2");

        _cache.Register(map1);
        _cache.Register(map2);

        _cache.GetStatistics().CachedMapCount.Should().Be(2);

        // Act
        _cache.Clear();

        // Assert
        _cache.GetStatistics().CachedMapCount.Should().Be(0);
        _cache.Contains(map1.Url).Should().BeFalse();
        _cache.Contains(map2.Url).Should().BeFalse();
    }

    #endregion

    #region Helper Methods

    private PackageResource CreateTestPackageResource(
        string url = null,
        string packageId = null,
        string packageVersion = null)
    {
        var mapUrl = url ?? TestMapUrl;
        var pkgId = packageId ?? TestPackageId;
        var pkgVersion = packageVersion ?? TestPackageVersion;

        var structureMapJson = $@"{{
            ""resourceType"": ""StructureMap"",
            ""url"": ""{mapUrl}"",
            ""name"": ""TestMap"",
            ""status"": ""active"",
            ""structure"": [
                {{
                    ""url"": ""http://hl7.org/fhir/StructureDefinition/Patient"",
                    ""mode"": ""source"",
                    ""alias"": ""Patient""
                }},
                {{
                    ""url"": ""http://hl7.org/fhir/StructureDefinition/Patient"",
                    ""mode"": ""target"",
                    ""alias"": ""PatientOut""
                }}
            ],
            ""group"": [
                {{
                    ""name"": ""Transform"",
                    ""typeMode"": ""none"",
                    ""input"": [
                        {{
                            ""name"": ""src"",
                            ""type"": ""Patient"",
                            ""mode"": ""source""
                        }},
                        {{
                            ""name"": ""tgt"",
                            ""type"": ""PatientOut"",
                            ""mode"": ""target""
                        }}
                    ],
                    ""rule"": [
                        {{
                            ""name"": ""copyId"",
                            ""source"": [
                                {{
                                    ""context"": ""src"",
                                    ""element"": ""id""
                                }}
                            ],
                            ""target"": [
                                {{
                                    ""context"": ""tgt"",
                                    ""element"": ""id""
                                }}
                            ]
                        }}
                    ]
                }}
            ]
        }}";

        return new PackageResource
        {
            PackageId = pkgId,
            PackageVersion = pkgVersion,
            ResourceType = "StructureMap",
            ResourceId = "TestMap",
            Canonical = mapUrl,
            ResourceJson = structureMapJson,
            FhirVersion = "4.0",
            LoadedDate = DateTimeOffset.UtcNow,
            IsActive = true
        };
    }

    private Ignixa.FhirMappingLanguage.Expressions.MapExpression CreateTestMapExpression(string url = null)
    {
        var mapUrl = url ?? TestMapUrl;

        var fmlText = $@"
map '{mapUrl}' = 'TestMap'

uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias PatientOut as target

group Transform(source src : Patient, target tgt : PatientOut) {{
  src.id -> tgt.id;
}}
";

        var parser = new MappingParser();
        return parser.Parse(fmlText);
    }

    #endregion
}

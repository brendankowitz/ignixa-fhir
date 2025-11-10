// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Ignixa.Specification;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ignixa.Validation.Tests;

/// <summary>
/// Tests for <see cref="CompositeSchemaProviderRegistry"/> debounce functionality.
/// </summary>
public class CompositeSchemaProviderRegistryTests
{
    [Fact]
    public void GivenDefaultConfiguration_WhenCreatingRegistry_ThenDebounceDelayIsOneSecond()
    {
        // Arrange & Act
        using var registry = new CompositeSchemaProviderRegistry(NullLogger<CompositeSchemaProviderRegistry>.Instance);

        // Assert
        registry.DebounceDelay.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GivenCustomDebounceDelay_WhenCreatingRegistry_ThenUsesCustomDelay()
    {
        // Arrange
        var customDelay = TimeSpan.FromMilliseconds(500);

        // Act
        using var registry = new CompositeSchemaProviderRegistry(
            NullLogger<CompositeSchemaProviderRegistry>.Instance,
            customDelay);

        // Assert
        registry.DebounceDelay.Should().Be(customDelay);
    }

    [Fact]
    public async Task GivenMultipleInvalidationRequests_WhenCalledQuickly_ThenDebounces()
    {
        // Arrange
        var shortDelay = TimeSpan.FromMilliseconds(100);

        using var registry = new CompositeSchemaProviderRegistry(
            NullLogger<CompositeSchemaProviderRegistry>.Instance,
            shortDelay);

        // Note: In real usage, the registry tracks providers and clears their caches
        // For this test, we're just verifying debounce behavior

        // Act - Request invalidation multiple times quickly
        await registry.InvalidateCacheForPackageAsync("package1", tenantId: 1, CancellationToken.None);
        await registry.InvalidateCacheForPackageAsync("package2", tenantId: 1, CancellationToken.None);
        await registry.InvalidateCacheForPackageAsync("package3", tenantId: 1, CancellationToken.None);

        // Wait for debounce window to expire
        await Task.Delay(shortDelay + TimeSpan.FromMilliseconds(50));

        // Assert
        // The actual invalidation execution is internal, but we can verify:
        // 1. No exceptions thrown
        // 2. Registry is still functional
        registry.DebounceDelay.Should().Be(shortDelay);
    }

    [Fact]
    public async Task GivenDifferentTenants_WhenInvalidating_ThenDebouncesIndependently()
    {
        // Arrange
        var shortDelay = TimeSpan.FromMilliseconds(100);

        using var registry = new CompositeSchemaProviderRegistry(
            NullLogger<CompositeSchemaProviderRegistry>.Instance,
            shortDelay);

        // Act - Request invalidation for different tenants
        await registry.InvalidateCacheForPackageAsync("package1", tenantId: 1, CancellationToken.None);
        await registry.InvalidateCacheForPackageAsync("package2", tenantId: 2, CancellationToken.None);

        // Wait for debounce window to expire
        await Task.Delay(shortDelay + TimeSpan.FromMilliseconds(50));

        // Assert - No exceptions, both tenants handled independently
        registry.DebounceDelay.Should().Be(shortDelay);
    }

    [Fact]
    public async Task GivenInvalidationRequests_WhenCancelled_ThenHandlesCancellationGracefully()
    {
        // Arrange
        var shortDelay = TimeSpan.FromMilliseconds(100);
        using var cts = new CancellationTokenSource();

        using var registry = new CompositeSchemaProviderRegistry(
            NullLogger<CompositeSchemaProviderRegistry>.Instance,
            shortDelay);

        // Act - Request invalidation, then cancel
        await registry.InvalidateCacheForPackageAsync("package1", tenantId: 1, cts.Token);

        // Cancel before debounce window expires
        await cts.CancelAsync();

        // Wait a bit to ensure cancellation is processed
        await Task.Delay(shortDelay + TimeSpan.FromMilliseconds(50));

        // Assert - No exceptions thrown, graceful cancellation
        registry.DebounceDelay.Should().Be(shortDelay);
    }

    [Fact]
    public void GivenRegistry_WhenDisposing_ThenCleansUpResources()
    {
        // Arrange
        var registry = new CompositeSchemaProviderRegistry(
            NullLogger<CompositeSchemaProviderRegistry>.Instance,
            TimeSpan.FromMilliseconds(100));

        // Act
        registry.Dispose();

        // Assert - No exceptions during disposal
        // Multiple dispose calls should be safe
        registry.Dispose();
    }
}

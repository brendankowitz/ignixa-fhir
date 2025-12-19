// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Collections.Frozen;
using Ignixa.Application.Features.Experimental.Ips.Api;

namespace Ignixa.Application.Features.Experimental.Ips.Registry;

/// <summary>
/// Registry of patient summary generation strategies.
/// </summary>
public class IpsGenerationStrategyRegistry : IIpsGenerationStrategyRegistry
{
    private readonly ConcurrentDictionary<string, IIpsGenerationStrategy> _strategies = new();
    private readonly IIpsGenerationStrategy _defaultStrategy;

    public IpsGenerationStrategyRegistry(IEnumerable<IIpsGenerationStrategy> strategies)
    {
        foreach (var strategy in strategies)
        {
            _strategies.TryAdd(strategy.BundleProfile, strategy);
        }

        _defaultStrategy = _strategies.Values.FirstOrDefault(s =>
            s.BundleProfile.Contains("uv/ips", StringComparison.Ordinal))
            ?? strategies.First();
    }

    public IIpsGenerationStrategy? GetStrategy(string? profileUrl)
    {
        if (profileUrl is null)
        {
            return null;
        }

        return _strategies.GetValueOrDefault(profileUrl);
    }

    public IIpsGenerationStrategy GetDefaultStrategy() => _defaultStrategy;

    public void RegisterStrategy(string profileUrl, IIpsGenerationStrategy strategy)
    {
        _strategies.AddOrUpdate(profileUrl, strategy, (_, _) => strategy);
    }

    public IReadOnlyDictionary<string, IIpsGenerationStrategy> GetAllStrategies()
    {
        return _strategies.ToFrozenDictionary();
    }
}

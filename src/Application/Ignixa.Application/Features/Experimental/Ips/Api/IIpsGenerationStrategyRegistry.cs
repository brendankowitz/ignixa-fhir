// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Experimental.Ips.Api;

/// <summary>
/// Registry of available patient summary generation strategies.
/// Strategies are registered when packages containing Composition profiles are installed.
/// </summary>
public interface IIpsGenerationStrategyRegistry
{
    /// <summary>
    /// Gets a strategy by its Bundle profile URL.
    /// </summary>
    IIpsGenerationStrategy? GetStrategy(string? profileUrl);

    /// <summary>
    /// Gets the default strategy (IPS).
    /// </summary>
    IIpsGenerationStrategy GetDefaultStrategy();

    /// <summary>
    /// Registers a strategy.
    /// </summary>
    void RegisterStrategy(string profileUrl, IIpsGenerationStrategy strategy);

    /// <summary>
    /// Lists all available strategies.
    /// </summary>
    IReadOnlyDictionary<string, IIpsGenerationStrategy> GetAllStrategies();
}

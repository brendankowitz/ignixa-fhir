// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Api;

/// <summary>
/// Marker class for creating loggers for Ignixa.Api endpoints.
/// Used as generic type parameter for ILogger{T} in endpoint methods.
/// </summary>
/// <remarks>
/// Previously endpoints used ILogger{Program}, but after moving Program.cs
/// to the Ignixa.Web hosting layer, this marker provides a stable type
/// for logging in the Ignixa.Api package.
/// </remarks>
internal sealed class IgnixaApiMarker
{
    private IgnixaApiMarker()
    {
        // Prevent instantiation - this is only a marker type
    }
}

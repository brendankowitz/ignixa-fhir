// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Ignixa.Api.E2ETests._Infrastructure;

/// <summary>
/// Server with <c>Serialization:OrderElementsOnWrite</c> enabled, so stored resources are spec-ordered.
/// </summary>
public sealed class ElementOrderingApiFixture : IgnixaApiFixture
{
    public ElementOrderingApiFixture() : base("ElementOrdering")
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Serialization:OrderElementsOnWrite"] = "true",
            }));
    }
}

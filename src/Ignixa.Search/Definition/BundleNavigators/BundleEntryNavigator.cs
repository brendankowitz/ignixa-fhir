// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Abstractions;

namespace Ignixa.Search.Definition.BundleNavigators;

internal class BundleEntryNavigator
{
    private readonly Lazy<IElement> _entry;

    internal BundleEntryNavigator(IElement entry)
    {
        EnsureArg.IsNotNull(entry, nameof(entry));

        // SDK 6.0 fix: Use Children() instead of Select() to avoid POCO conversion issues
        _entry = new Lazy<IElement>(() =>
        {
            var children = entry.Children("resource");
            return children.Count > 0 ? children[0] : null;
        });
    }

    public IElement Resource => _entry.Value;
}

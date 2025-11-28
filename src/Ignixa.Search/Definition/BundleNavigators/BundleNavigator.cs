// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Abstractions;

namespace Ignixa.Search.Definition.BundleNavigators;

internal class BundleNavigator
{
    private readonly Lazy<IReadOnlyList<BundleEntryNavigator>> _entries;

    public BundleNavigator(IElement bundle)
    {
        EnsureArg.IsNotNull(bundle, nameof(bundle));
        EnsureArg.Is(KnownResourceTypes.Bundle, bundle.InstanceType, StringComparison.Ordinal, nameof(bundle));

        // SDK 6.0 fix: Use Children() instead of Select() to avoid POCO conversion issues
        // Select("entry") tries to convert to Bundle.EntryComponent which is abstract
        _entries = new Lazy<IReadOnlyList<BundleEntryNavigator>>(() =>
        {
            var entries = new List<BundleEntryNavigator>();
            foreach (var child in bundle.Children("entry"))
            {
                entries.Add(new BundleEntryNavigator(child));
            }
            return entries;
        });
    }

    public IReadOnlyList<BundleEntryNavigator> Entries => _entries.Value;
}

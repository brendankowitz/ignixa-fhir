// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.SourceNodeSerialization.Abstractions;

namespace Ignixa.Search.Definition.BundleNavigators;

internal class BundleEntryNavigator
{
    private readonly Lazy<ITypedElement> _entry;

    internal BundleEntryNavigator(ITypedElement entry)
    {
        EnsureArg.IsNotNull(entry, nameof(entry));

        // SDK 6.0 fix: Use Children() instead of Select() to avoid POCO conversion issues
        _entry = new Lazy<ITypedElement>(() => entry.Children("resource").FirstOrDefault());
    }

    public ITypedElement Resource => _entry.Value;
}

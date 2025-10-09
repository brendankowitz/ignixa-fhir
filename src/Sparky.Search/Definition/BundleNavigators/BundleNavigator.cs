// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Sparky.Extensions.Models;

namespace Sparky.Search.Definition.BundleNavigators;

internal class BundleNavigator
{
    private readonly Lazy<IReadOnlyList<BundleEntryNavigator>> _entries;

    public BundleNavigator(ITypedElement bundle)
    {
        EnsureArg.IsNotNull(bundle, nameof(bundle));
        EnsureArg.Is(KnownResourceTypes.Bundle, bundle.InstanceType, StringComparison.Ordinal, nameof(bundle));

        _entries = new Lazy<IReadOnlyList<BundleEntryNavigator>>(() => bundle.Select("entry").Select(x => new BundleEntryNavigator(x)).ToArray());
    }

    public IReadOnlyList<BundleEntryNavigator> Entries => _entries.Value;
}

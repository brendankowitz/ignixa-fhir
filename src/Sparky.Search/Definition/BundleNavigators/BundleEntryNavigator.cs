// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;

namespace Sparky.Search.Definition.BundleNavigators;

internal class BundleEntryNavigator
{
    private readonly Lazy<ITypedElement> _entry;

    internal BundleEntryNavigator(ITypedElement entry)
    {
        EnsureArg.IsNotNull(entry, nameof(entry));

        _entry = new Lazy<ITypedElement>(() => entry.Select("resource").FirstOrDefault());
    }

    public ITypedElement Resource => _entry.Value;
}

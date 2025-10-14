// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Sparky.Domain.ElementModel;
using Sparky.Search.Indexing.SearchValues;

namespace Sparky.Search.Indexing.Converters;

public interface ITypedElementToSearchValueConverter
{
    IReadOnlyList<string> FhirTypes { get; }

    Type SearchValueType { get; }

    IEnumerable<ISearchValue> ConvertTo(ITypedElement value);
}

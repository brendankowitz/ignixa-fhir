// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Abstractions;

namespace Ignixa.Search.Indexing.Converters;

public interface ITypedElementToSearchValueConverter
{
    IReadOnlyList<string> FhirTypes { get; }

    Type SearchValueType { get; }

    IEnumerable<ISearchValue> ConvertTo(ITypedElement value);
}

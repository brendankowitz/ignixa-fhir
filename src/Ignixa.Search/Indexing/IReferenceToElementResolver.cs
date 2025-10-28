// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.Abstractions;

namespace Ignixa.Search.Indexing;

public interface IReferenceToElementResolver
{
    ITypedElement Resolve(string reference);
}

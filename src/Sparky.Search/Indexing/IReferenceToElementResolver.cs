// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Sparky.SourceNodeSerialization.ElementModel;

namespace Sparky.Search.Indexing;

public interface IReferenceToElementResolver
{
    ITypedElement Resolve(string reference);
}

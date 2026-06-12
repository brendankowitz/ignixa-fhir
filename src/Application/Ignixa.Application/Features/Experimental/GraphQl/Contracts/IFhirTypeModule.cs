// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Experimental.GraphQl.Contracts;

public interface IFhirTypeModule
{
    event EventHandler<EventArgs>? TypesChanged;

    void NotifyTypesChanged();
}

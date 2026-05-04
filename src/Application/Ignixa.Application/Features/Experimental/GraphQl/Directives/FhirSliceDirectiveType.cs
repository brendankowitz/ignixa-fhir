// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using HotChocolate.Types;

namespace Ignixa.Application.Features.Experimental.GraphQl.Directives;

public sealed class FhirSliceDirectiveType : DirectiveType
{
    protected override void Configure(IDirectiveTypeDescriptor descriptor)
    {
        descriptor.Name("slice");
        descriptor.Description("Split a list into named singletons using a FHIRPath discriminator.");
        descriptor.Location(DirectiveLocation.Field);
        descriptor.Argument("path").Type<NonNullType<StringType>>()
            .Description("FHIRPath expression to evaluate on each element as the discriminator suffix.");
    }
}

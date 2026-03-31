// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using HotChocolate.Types;

namespace Ignixa.Application.Features.Experimental.GraphQl.Directives;

public sealed class FhirFlattenDirectiveType : DirectiveType
{
    protected override void Configure(IDirectiveTypeDescriptor descriptor)
    {
        descriptor.Name("flatten");
        descriptor.Description("Hoist children up to parent level. Children become lists.");
        descriptor.Location(DirectiveLocation.Field);
    }
}

public sealed class FhirFirstDirectiveType : DirectiveType
{
    protected override void Configure(IDirectiveTypeDescriptor descriptor)
    {
        descriptor.Name("first");
        descriptor.Description("Select only the first element from a repeating list.");
        descriptor.Location(DirectiveLocation.Field);
    }
}

public sealed class FhirSingletonDirectiveType : DirectiveType
{
    protected override void Configure(IDirectiveTypeDescriptor descriptor)
    {
        descriptor.Name("singleton");
        descriptor.Description("Assert single value after flattening. Error if more than one.");
        descriptor.Location(DirectiveLocation.Field);
    }
}

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

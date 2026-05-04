// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using HotChocolate.Types;

namespace Ignixa.Application.Features.Experimental.GraphQl.Directives;

public sealed class FhirSingletonDirectiveType : DirectiveType
{
    protected override void Configure(IDirectiveTypeDescriptor descriptor)
    {
        descriptor.Name("singleton");
        descriptor.Description("Assert single value after flattening. Error if more than one.");
        descriptor.Location(DirectiveLocation.Field);
        // @singleton is applied in FlattenResultProcessor post-processing, not as HC middleware.
        // HC middleware can't change a list-typed field to a single element without violating
        // the schema type system, which throws "Unexpected Execution Error" in HC 15.
    }
}

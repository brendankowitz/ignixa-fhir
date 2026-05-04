// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using HotChocolate.Types;

namespace Ignixa.Application.Features.Experimental.GraphQl.Directives;

public sealed class FhirFirstDirectiveType : DirectiveType
{
    protected override void Configure(IDirectiveTypeDescriptor descriptor)
    {
        descriptor.Name("first");
        descriptor.Description("Select only the first element from a repeating list.");
        descriptor.Location(DirectiveLocation.Field);
        // @first is applied in FlattenResultProcessor post-processing, not as HC middleware.
        // HC middleware can't change a list-typed field to a single element without violating
        // the schema type system, which throws "Unexpected Execution Error" in HC 15.
    }
}

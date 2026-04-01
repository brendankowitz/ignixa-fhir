// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using HotChocolate;
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
        // @first is applied in FlattenResultProcessor post-processing, not as HC middleware.
        // HC middleware can't change a list-typed field to a single element without violating
        // the schema type system, which throws "Unexpected Execution Error" in HC 15.
    }
}

public sealed class FhirSingletonDirectiveType : DirectiveType
{
    protected override void Configure(IDirectiveTypeDescriptor descriptor)
    {
        descriptor.Name("singleton");
        descriptor.Description("Assert single value after flattening. Error if more than one.");
        descriptor.Location(DirectiveLocation.Field);
        descriptor.Use((next, _) => async context =>
        {
            await next(context);
            context.Result = FhirDirectiveMiddleware.ApplySingleton(context.Result);
        });
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

internal static class FhirDirectiveMiddleware
{
    internal static object? ApplyFirst(object? result)
    {
        if (result is IList<JsonElement> jsonList)
            return jsonList.Count > 0 ? jsonList[0] : null;
        if (result is IEnumerable<object> enumerable)
            return enumerable.FirstOrDefault();
        return result;
    }

    internal static object? ApplySingleton(object? result)
    {
        if (result is IList<JsonElement> jsonList)
        {
            if (jsonList.Count > 1)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage($"@singleton expects at most 1 element but found {jsonList.Count}")
                        .SetCode("FHIR_SINGLETON_VIOLATION")
                        .Build());
            }
            return jsonList.Count == 1 ? jsonList[0] : null;
        }
        return result;
    }
}

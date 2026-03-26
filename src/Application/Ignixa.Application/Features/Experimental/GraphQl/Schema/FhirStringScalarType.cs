// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using HotChocolate.Language;
using HotChocolate.Types;

namespace Ignixa.Application.Features.Experimental.GraphQl.Schema;

internal abstract class FhirStringScalarType : ScalarType<string, StringValueNode>
{
    protected FhirStringScalarType(string name, string description) : base(name)
    {
        Description = description;
    }

    public override IValueNode ParseResult(object? resultValue)
        => resultValue is string s ? new StringValueNode(s) : NullValueNode.Default;

    protected override string ParseLiteral(StringValueNode valueSyntax)
        => valueSyntax.Value;

    protected override StringValueNode ParseValue(string runtimeValue)
        => new(runtimeValue);
}

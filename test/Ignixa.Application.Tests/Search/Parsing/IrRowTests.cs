// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Parsing;

public class IrRowTests
{
    [Fact]
    public void GivenANegativeDepth_WhenConstructed_ThenItThrows()
        => Should.Throw<ArgumentOutOfRangeException>(() => new IrRow("and", "And", -1));

    [Fact]
    public void GivenAnEmptyKindOrText_WhenConstructed_ThenItThrows()
    {
        Should.Throw<ArgumentException>(() => new IrRow(string.Empty, "And", 0));
        Should.Throw<ArgumentException>(() => new IrRow("and", string.Empty, 0));
    }
}

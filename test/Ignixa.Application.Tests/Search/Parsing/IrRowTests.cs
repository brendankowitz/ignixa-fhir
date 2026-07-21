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
    public void GivenAnEmptyKind_WhenConstructed_ThenItThrows()
        => Should.Throw<ArgumentException>(() => new IrRow(string.Empty, "And", 0));

    [Fact]
    public void GivenANullText_WhenConstructed_ThenItThrows()
        => Should.Throw<ArgumentNullException>(() => new IrRow("and", null!, 0));

    [Fact]
    public void GivenAnEmptyText_WhenConstructed_ThenItIsAccepted()
    {
        // Arrange & Act -- IrProjector.TextOf falls back to an empty string rather than failing a trace
        // over a node that renders blank. A guard here would turn that fallback into a guaranteed throw.
        var row = new IrRow("stringField", string.Empty, 0);

        // Assert
        row.Text.ShouldBe(string.Empty);
    }
}

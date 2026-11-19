// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Parsing;
using Ignixa.FhirPath.Tests.TestHelpers;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Parsing;

/// <summary>
/// <see cref="FhirPathParser.TryParse"/> reports a parse failure as <see langword="false"/>, but running out of
/// stack while building the syntax tree is not a parse failure and propagates (#538).
/// </summary>
/// <remarks>
/// The chain is longer than the default limits allow, so the limits are raised: a default-configured parser
/// rejects it with a <see cref="FormatException"/> before the AST builder ever recurses over it.
/// </remarks>
public class TryParseCriticalExceptionTests
{
    private const int ChainLength = 20_000;

    [Fact]
    public void GivenATreeTooDeepForTheThreadStack_WhenTryParsing_ThenInsufficientExecutionStackPropagates()
    {
        // Arrange
        var parser = new FhirPathParser(new CompilationOptions
        {
            MaxExpressionLength = int.MaxValue,
            MaxExpressionDepth = int.MaxValue,
        });
        var chain = "a" + string.Concat(Enumerable.Repeat(".a", ChainLength));

        // Act & Assert
        Should.Throw<InsufficientExecutionStackException>(
            () => StackSizedThread.Run(() => parser.TryParse(chain, out _, out _), maxStackSize: 1024 * 1024));
    }
}

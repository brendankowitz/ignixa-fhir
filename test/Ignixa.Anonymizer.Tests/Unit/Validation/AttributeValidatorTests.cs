// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.Validation
{
    /// <summary>
    /// AttributeValidator was emptied in the Ignixa migration since POCO-based
    /// validation does not apply. The Ignixa SDK validates at the JSON/schema level.
    /// These tests are retained as placeholders to verify the test infrastructure compiles.
    /// </summary>
    public class AttributeValidatorTests
    {
        [Fact]
        public void AttributeValidator_WasRemovedInIgnixaMigration_TestIsPlaceholder()
        {
            // AttributeValidator was emptied since POCO validation doesn't apply in Ignixa.
            // Validation is now handled at the schema/JSON level by the Ignixa SDK.
            Assert.True(true);
        }
    }
}

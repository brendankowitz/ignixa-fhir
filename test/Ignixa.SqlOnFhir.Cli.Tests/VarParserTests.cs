// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.SqlOnFhir.Cli;
using Shouldly;

namespace Ignixa.SqlOnFhir.Cli.Tests;

public class VarParserTests
{
    [Fact]
    public void GivenNullInput_WhenParsing_ThenReturnsEmpty()
        => VarParser.Parse(null).ShouldBeEmpty();

    [Fact]
    public void GivenValidPairs_WhenParsing_ThenReturnsDictionary()
    {
        var result = VarParser.Parse(["effectiveDate=2024-01-01", "cohortId=COHORT_A"]);
        result["effectiveDate"].ShouldBe("2024-01-01");
        result["cohortId"].ShouldBe("COHORT_A");
    }

    [Fact]
    public void GivenValueContainingEquals_WhenParsing_ThenUsesFirstEqualsAsDelimiter()
    {
        var result = VarParser.Parse(["url=http://example.com/path?a=b"]);
        result["url"].ShouldBe("http://example.com/path?a=b");
    }

    [Fact]
    public void GivenMissingEquals_WhenParsing_ThenThrowsArgumentException()
        => Should.Throw<ArgumentException>(() => VarParser.Parse(["noequals"]));

    [Fact]
    public void GivenEmptyName_WhenParsing_ThenThrowsArgumentException()
        => Should.Throw<ArgumentException>(() => VarParser.Parse(["=value"]));

    [Fact]
    public void GivenEmptyValue_WhenParsing_ThenValueIsEmptyString()
        => VarParser.Parse(["name="])["name"].ShouldBe("");
}

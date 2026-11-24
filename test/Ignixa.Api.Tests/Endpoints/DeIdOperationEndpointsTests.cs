// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Api.Endpoints;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace Ignixa.Api.Tests.Endpoints;

public class DeIdOperationEndpointsTests
{
    [Fact]
    public void GivenWarningsWithNonAsciiAndLineBreaks_WhenAddingWarningHeader_ThenHeaderIsAFixedAsciiCount()
    {
        // Regression: warning text (which can embed resource values) was written to the header, so
        // non-ASCII, CR/LF or U+2028 made the response fail after de-identification had completed.
        var context = new DefaultHttpContext();
        string[] warnings =
        [
            "Processor failed for rule 'Patient.name': value 'José' invalid",
            "line one\r\nInjected: header",
            "separator" + char.ConvertFromUtf32(0x2028) + "here"
        ];

        DeIdOperationEndpoints.AddWarningHeader(context, warnings);

        var value = context.Response.Headers[DeIdOperationEndpoints.DeIdWarningHeader].ToString();
        value.ShouldBe("count=3");
        value.All(c => c < 128 && !char.IsControl(c)).ShouldBeTrue();
    }

    [Fact]
    public void GivenAtLeastOneWarning_WhenAddingWarningHeader_ThenTheResponseCarriesTheHeader()
    {
        var context = new DefaultHttpContext();

        DeIdOperationEndpoints.AddWarningHeader(context, ["w"]);

        context.Response.Headers.ContainsKey(DeIdOperationEndpoints.DeIdWarningHeader).ShouldBeTrue();
    }

    [Fact]
    public void GivenNoWarnings_WhenAddingWarningHeader_ThenNoHeaderIsAdded()
    {
        var context = new DefaultHttpContext();

        DeIdOperationEndpoints.AddWarningHeader(context, []);
        DeIdOperationEndpoints.AddWarningHeader(context, null);

        context.Response.Headers.ContainsKey(DeIdOperationEndpoints.DeIdWarningHeader).ShouldBeFalse();
    }
}

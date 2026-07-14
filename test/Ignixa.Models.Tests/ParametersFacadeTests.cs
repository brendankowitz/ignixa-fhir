// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

public sealed class ParametersFacadeTests
{
    [Fact]
    public void GivenParameters_WhenReadBack_ThenSharedFieldsRoundTrip()
    {
        var parameters = new Parameters();
        var parameter = new ParametersParameter { Name = "resourceType" };
        parameter.SetValue("valueString", "Patient");
        parameters.Parameter.Add(parameter);

        parameters.Parameter.Single().Name.ShouldBe("resourceType");
        parameters.Parameter.Single().GetValueAs<string>("valueString").ShouldBe("Patient");
    }

    [Fact]
    public void GivenParameters_WhenFindParameterCalled_ThenReturnsMatchingParameterByName()
    {
        var parameters = new Parameters();
        parameters.Parameter.Add(new ParametersParameter { Name = "first" });
        parameters.Parameter.Add(new ParametersParameter { Name = "second" });

        parameters.FindParameter("second").Name.ShouldBe("second");
    }

    [Fact]
    public void GivenParameters_WhenFindParameterCalledWithMissingName_ThenReturnsNull()
    {
        var parameters = new Parameters();
        parameters.Parameter.Add(new ParametersParameter { Name = "first" });

        parameters.FindParameter("missing").ShouldBeNull();
    }

    [Fact]
    public void GivenParametersParameter_WhenFindPartCalled_ThenReturnsMatchingPartByName()
    {
        var parameter = new ParametersParameter { Name = "operation" };
        parameter.Part.Add(new ParametersParameter { Name = "type" });
        parameter.Part.Add(new ParametersParameter { Name = "path" });

        parameter.FindPart("path").Name.ShouldBe("path");
    }

    [Fact]
    public void GivenParametersParameter_WhenGetValueCalled_ThenReturnsFirstValueXField()
    {
        var parameter = new ParametersParameter();
        parameter.SetValue("valueCode", "replace");

        parameter.GetValue().GetValue<string>().ShouldBe("replace");
        parameter.GetValueAs<string>().ShouldBe("replace");
        parameter.GetValueAs<string>("valueCode").ShouldBe("replace");
    }

    [Fact]
    public void GivenParametersParameter_WhenSetValueCalledWithJsonNode_ThenValueIsStored()
    {
        var parameter = new ParametersParameter();

        parameter.SetValue("valueReference", new JsonObject { ["reference"] = "Patient/123" });

        parameter.GetValue("valueReference")!["reference"]!.GetValue<string>().ShouldBe("Patient/123");
    }
}

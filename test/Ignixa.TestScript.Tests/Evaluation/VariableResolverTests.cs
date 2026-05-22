using System.Collections.Immutable;
using Ignixa.TestScript.Evaluation;

namespace Ignixa.TestScript.Tests.Evaluation;

public class VariableResolverTests
{
    private static TestScriptContext CreateContext(params (string key, string value)[] variables)
    {
        var vars = ImmutableDictionary<string, string>.Empty;
        foreach (var (key, value) in variables)
            vars = vars.SetItem(key, value);

        return new TestScriptContext
        {
            Variables = vars
        };
    }

    [Fact]
    public void GivenInputWithVariable_WhenResolving_ThenSubstitutesValue()
    {
        var ctx = CreateContext(("patientId", "123"));
        var result = VariableResolver.Resolve("Patient/${patientId}", ctx);
        result.ShouldBe("Patient/123");
    }

    [Fact]
    public void GivenInputWithMultipleVariables_WhenResolving_ThenSubstitutesAll()
    {
        var ctx = CreateContext(("type", "Patient"), ("id", "456"));
        var result = VariableResolver.Resolve("${type}/${id}", ctx);
        result.ShouldBe("Patient/456");
    }

    [Fact]
    public void GivenInputWithNoVariables_WhenResolving_ThenReturnsUnchanged()
    {
        var ctx = CreateContext();
        var result = VariableResolver.Resolve("Patient/123", ctx);
        result.ShouldBe("Patient/123");
    }

    [Fact]
    public void GivenUndefinedVariable_WhenResolving_ThenThrows()
    {
        var ctx = CreateContext();
        Should.Throw<InvalidOperationException>(() =>
            VariableResolver.Resolve("Patient/${missing}", ctx));
    }

    [Fact]
    public void GivenEscapedVariable_WhenResolving_ThenDoesNotSubstitute()
    {
        var ctx = CreateContext(("x", "val"));
        var result = VariableResolver.Resolve(@"literal \${x} here", ctx);
        result.ShouldBe(@"literal \${x} here");
    }

    [Fact]
    public void GivenNullInput_WhenResolvingIfNotNull_ThenReturnsNull()
    {
        var ctx = CreateContext();
        VariableResolver.ResolveIfNotNull(null, ctx).ShouldBeNull();
    }
}

using System.Text.RegularExpressions;

namespace Ignixa.TestScript.Evaluation;

public static partial class VariableResolver
{
    [GeneratedRegex(@"(?<!\\)\$\{([^}]+)\}")]
    private static partial Regex VariablePattern();

    public static string Resolve(string input, TestScriptContext context)
    {
        return VariablePattern().Replace(input, match =>
        {
            var varName = match.Groups[1].Value;
            if (context.Variables.TryGetValue(varName, out var value))
                return value;

            throw new InvalidOperationException(
                $"Variable '${{{varName}}}' is not defined. " +
                $"Available variables: {string.Join(", ", context.Variables.Keys)}");
        });
    }

    public static string? ResolveIfNotNull(string? input, TestScriptContext context) =>
        input is null ? null : Resolve(input, context);
}

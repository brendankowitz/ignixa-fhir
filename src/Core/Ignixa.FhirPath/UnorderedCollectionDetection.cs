using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath;

internal static class UnorderedCollectionDetection
{
    internal static string? GetUnorderedNavigationSource(Expression? focus) =>
        focus switch
        {
            null => null,
            ParenthesizedExpression p => GetUnorderedNavigationSource(p.InnerExpression),
            IndexerExpression idx => GetUnorderedNavigationSource(idx.Focus),
            FunctionCallExpression fn when IsUnorderedSource(fn.FunctionName) => fn.FunctionName,
            FunctionCallExpression fn when IsOrderIntroducing(fn.FunctionName) => null,
            FunctionCallExpression fn => GetUnorderedNavigationSource(fn.Focus),
            _ => null
        };

    internal static bool IsOrderDependentFunction(string functionName) =>
        IsPositionalFunction(functionName) ||
        functionName.Equals("first", StringComparison.OrdinalIgnoreCase) ||
        functionName.Equals("last", StringComparison.OrdinalIgnoreCase);

    internal static bool IsPositionalFunction(string functionName) =>
        functionName.Equals("skip", StringComparison.OrdinalIgnoreCase) ||
        functionName.Equals("take", StringComparison.OrdinalIgnoreCase) ||
        functionName.Equals("tail", StringComparison.OrdinalIgnoreCase);

    internal static bool IsUnorderedSource(string functionName) =>
        functionName.Equals("children", StringComparison.OrdinalIgnoreCase) ||
        functionName.Equals("descendants", StringComparison.OrdinalIgnoreCase);

    internal static bool IsOrderIntroducing(string functionName) =>
        functionName.Equals("sort", StringComparison.OrdinalIgnoreCase) ||
        functionName.Equals("sortBy", StringComparison.OrdinalIgnoreCase);
}

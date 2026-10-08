using System.Data.Common;

namespace Ignixa.Application.Features.Conformance;

public sealed class ReindexTriggerUnavailableException(string message, Exception innerException)
    : Exception(message, innerException)
{
    // Application intentionally has no Azure SDK or DurableTask provider dependency. The provider's
    // internal storage exception is therefore identified by its stable fully qualified type name.
    private const string RequestFailedExceptionTypeName = "Azure.RequestFailedException";
    private const string DurableTaskStorageExceptionTypeName =
        "DurableTask.AzureStorage.Storage.DurableTaskStorageException";
    private const string OrchestrationFrameworkExceptionTypeName =
        "DurableTask.Core.Exceptions.OrchestrationFrameworkException";
    private const string OrchestrationAlreadyExistsExceptionTypeName =
        "DurableTask.Core.Exceptions.OrchestrationAlreadyExistsException";

    public static bool IsOperational(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return !IsNonOperational(exception)
            && GetExceptionChain(exception).Any(IsOperationalFailure);
    }

    private static bool IsNonOperational(Exception exception) =>
        exception is ArgumentException
            or InvalidOperationException
            or NullReferenceException
            or OperationCanceledException
        || IsTypeOrBaseType(exception, OrchestrationAlreadyExistsExceptionTypeName);

    private static bool IsOperationalFailure(Exception exception) =>
        exception is DbException or IOException or TimeoutException
        || IsTypeOrBaseType(exception, RequestFailedExceptionTypeName)
        || IsTypeOrBaseType(exception, DurableTaskStorageExceptionTypeName)
        || IsTypeOrBaseType(exception, OrchestrationFrameworkExceptionTypeName);

    private static IEnumerable<Exception> GetExceptionChain(Exception exception)
    {
        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(exception);

        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current))
            {
                continue;
            }

            yield return current;

            if (current is AggregateException aggregateException)
            {
                foreach (var innerException in aggregateException.InnerExceptions)
                {
                    pending.Push(innerException);
                }
            }
            else if (current.InnerException is not null)
            {
                pending.Push(current.InnerException);
            }
        }
    }

    private static bool IsTypeOrBaseType(Exception exception, string fullTypeName)
    {
        for (Type? type = exception.GetType(); type is not null; type = type.BaseType)
        {
            if (type.FullName == fullTypeName)
            {
                return true;
            }
        }

        return false;
    }
}

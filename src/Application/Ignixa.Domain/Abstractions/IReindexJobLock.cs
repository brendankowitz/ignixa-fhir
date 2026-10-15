namespace Ignixa.Domain.Abstractions;

public interface IReindexJobLock
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);
}

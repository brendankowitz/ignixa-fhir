// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.DeId.Models;

/// <summary>
/// Represents the result of an operation that can either succeed with a value or fail with an error.
/// </summary>
/// <typeparam name="TValue">The type of the success value.</typeparam>
public readonly record struct Result<TValue>
{
    private readonly TValue? _value;
    private readonly DeIdError? _error;

    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the success value. Throws if the result is a failure.
    /// </summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access value of failed result");

    /// <summary>
    /// Gets the error. Throws if the result is a success.
    /// </summary>
    public DeIdError Error => !IsSuccess
        ? _error!
        : throw new InvalidOperationException("Cannot access error of successful result");

    private Result(TValue value)
    {
        _value = value;
        _error = default;
        IsSuccess = true;
    }

    private Result(DeIdError error)
    {
        _value = default;
        _error = error;
        IsSuccess = false;
    }

    /// <summary>
    /// Creates a successful result with the given value.
    /// </summary>
#pragma warning disable CA1000 // Do not declare static members on generic types
    public static Result<TValue> Success(TValue value) => new(value);
#pragma warning restore CA1000

    /// <summary>
    /// Creates a failed result with the given error.
    /// </summary>
#pragma warning disable CA1000 // Do not declare static members on generic types
    public static Result<TValue> Failure(DeIdError error) => new(error);
#pragma warning restore CA1000

    /// <summary>
    /// Pattern matches the result, executing the appropriate function based on success or failure.
    /// </summary>
    public TResult Match<TResult>(
        Func<TValue, TResult> onSuccess,
        Func<DeIdError, TResult> onFailure)
        => IsSuccess ? onSuccess(_value!) : onFailure(_error!);
}

/// <summary>
/// Represents an error that occurred during de-identification.
/// </summary>
public sealed record DeIdError(
    string Code,
    string Message,
    ErrorSeverity Severity = ErrorSeverity.Error,
    Exception? Exception = null,
    string? Path = null);

/// <summary>
/// Severity level of an de-identification error.
/// </summary>
public enum ErrorSeverity
{
    Warning,
    Error,
    Fatal
}

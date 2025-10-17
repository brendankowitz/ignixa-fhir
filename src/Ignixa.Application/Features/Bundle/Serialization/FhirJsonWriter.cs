// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using EnsureThat;

namespace Ignixa.Application.Features.Bundle.Serialization;

/// <summary>
/// A fluent wrapper around Utf8JsonWriter for streaming FHIR JSON serialization.
/// Provides a clean, chainable API for writing JSON with conditional logic support.
/// </summary>
internal class FhirJsonWriter : IDisposable, IAsyncDisposable
{
    private readonly JsonWriterOptions _writerOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly JsonWriterOptions _indentedWriterOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = true,
    };

    private readonly Utf8JsonWriter _writer;

    private FhirJsonWriter(Stream outputStream, bool pretty = false)
    {
        _writer = new Utf8JsonWriter(outputStream, pretty ? _indentedWriterOptions : _writerOptions);
    }

    /// <summary>
    /// Creates a new FhirJsonWriter for streaming JSON to the output stream.
    /// </summary>
    /// <param name="outputStream">The stream to write JSON to.</param>
    /// <param name="pretty">Whether to format JSON with indentation.</param>
    /// <returns>A new FhirJsonWriter instance.</returns>
    public static FhirJsonWriter Create(Stream outputStream, bool pretty = false)
    {
        return new FhirJsonWriter(outputStream, pretty);
    }

    /// <summary>
    /// Writes the start of a JSON object.
    /// </summary>
    public FhirJsonWriter WriteStartObject()
    {
        _writer.WriteStartObject();
        return this;
    }

    /// <summary>
    /// Writes the start of a JSON object property.
    /// </summary>
    public FhirJsonWriter WriteStartObject(string propertyName)
    {
        EnsureArg.IsNotNullOrEmpty(propertyName, nameof(propertyName));
        _writer.WriteStartObject(propertyName);
        return this;
    }

    /// <summary>
    /// Writes the end of a JSON object.
    /// </summary>
    public FhirJsonWriter WriteEndObject()
    {
        _writer.WriteEndObject();
        return this;
    }

    /// <summary>
    /// Writes a complete JSON object property with the provided action.
    /// </summary>
    public FhirJsonWriter WriteObject(string propertyName, Action<FhirJsonWriter> action)
    {
        EnsureArg.IsNotNullOrEmpty(propertyName, nameof(propertyName));
        EnsureArg.IsNotNull(action, nameof(action));

        _writer.WriteStartObject(propertyName);
        action(this);
        _writer.WriteEndObject();
        return this;
    }

    /// <summary>
    /// Writes the start of a JSON array property.
    /// </summary>
    public FhirJsonWriter WriteStartArray(string propertyName)
    {
        EnsureArg.IsNotNullOrEmpty(propertyName, nameof(propertyName));
        _writer.WriteStartArray(propertyName);
        return this;
    }

    /// <summary>
    /// Writes the end of a JSON array.
    /// </summary>
    public FhirJsonWriter WriteEndArray()
    {
        _writer.WriteEndArray();
        return this;
    }

    /// <summary>
    /// Writes a string property, skipping if the value is null or empty.
    /// </summary>
    public FhirJsonWriter WriteOptionalString(string name, string? value)
    {
        EnsureArg.IsNotNullOrEmpty(name, nameof(name));

        if (!string.IsNullOrEmpty(value))
        {
            _writer.WriteString(name, value);
        }

        return this;
    }

    /// <summary>
    /// Writes a string property (value must not be null or empty).
    /// </summary>
    public FhirJsonWriter WriteString(string name, string value)
    {
        EnsureArg.IsNotNullOrEmpty(name, nameof(name));
        EnsureArg.IsNotNullOrEmpty(value, nameof(value));

        return WriteOptionalString(name, value);
    }

    /// <summary>
    /// Writes a number property.
    /// </summary>
    public FhirJsonWriter WriteNumber(string name, int value)
    {
        EnsureArg.IsNotNullOrEmpty(name, nameof(name));

        _writer.WriteNumber(name, value);

        return this;
    }

    /// <summary>
    /// Writes a number property, skipping if the value is null.
    /// </summary>
    public FhirJsonWriter WriteOptionalNumber(string name, int? value)
    {
        EnsureArg.IsNotNullOrEmpty(name, nameof(name));

        if (value.HasValue)
        {
            _writer.WriteNumber(name, value.Value);
        }

        return this;
    }

    /// <summary>
    /// Writes a raw JSON property value from pre-serialized UTF-8 bytes.
    /// This enables zero-copy passthrough of already-serialized JSON.
    /// Strips UTF-8 BOM if present for backward compatibility with legacy files.
    /// </summary>
    public FhirJsonWriter WriteRawProperty(string name, ReadOnlyMemory<byte> value)
    {
        EnsureArg.IsNotNullOrEmpty(name, nameof(name));

        _writer.WritePropertyName(name);

        // Strip UTF-8 BOM if present (0xEF 0xBB 0xBF)
        // This handles legacy files written with BOM while being zero-cost for clean files
        var span = value.Span;
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
        {
            span = span.Slice(3); // Skip the 3-byte UTF-8 BOM
        }

        _writer.WriteRawValue(span, skipInputValidation: false);

        return this;
    }

    /// <summary>
    /// Writes an array property with items serialized using the provided action.
    /// </summary>
    public FhirJsonWriter WriteArray<T>(string name, IEnumerable<T> values, Action<FhirJsonWriter, T> itemWriter)
    {
        EnsureArg.IsNotNullOrEmpty(name, nameof(name));
        EnsureArg.IsNotNull(values, nameof(values));
        EnsureArg.IsNotNull(itemWriter, nameof(itemWriter));

        _writer.WriteStartArray(name);

        foreach (T item in values)
        {
            _writer.WriteStartObject();
            itemWriter(this, item);
            _writer.WriteEndObject();
        }

        _writer.WriteEndArray();

        return this;
    }

    /// <summary>
    /// Conditionally executes the provided action if the predicate is true.
    /// </summary>
    /// <param name="predicate">Condition to test.</param>
    /// <param name="action">Action to execute if condition is true.</param>
    /// <returns>This FhirJsonWriter instance for chaining.</returns>
    public FhirJsonWriter Condition(bool predicate, Action<FhirJsonWriter> action)
    {
        EnsureArg.IsNotNull(action, nameof(action));

        if (predicate)
        {
            action(this);
        }

        return this;
    }

    /// <summary>
    /// Conditionally executes the provided action if the predicate is true.
    /// Returns a BundleIfElse for chaining ElseIf conditions.
    /// </summary>
    /// <param name="predicate">Condition to test.</param>
    /// <param name="action">Action to execute if condition is true.</param>
    /// <returns>A BundleIfElse instance for chaining ElseIf conditions.</returns>
    public BundleIfElse ConditionIf(bool predicate, Action<FhirJsonWriter> action)
    {
        EnsureArg.IsNotNull(action, nameof(action));

        if (predicate)
        {
            action(this);
        }

        return new BundleIfElse(this, predicate);
    }

    /// <summary>
    /// Flushes the writer to the underlying stream asynchronously.
    /// Use this to incrementally stream data to the client.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _writer.FlushAsync(cancellationToken);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _writer?.Dispose();
        }
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_writer != null)
        {
            await _writer.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }
}

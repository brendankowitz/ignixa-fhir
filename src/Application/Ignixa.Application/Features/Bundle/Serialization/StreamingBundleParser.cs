// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Ignixa.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Bundle.Serialization;

/// <summary>
/// Streaming JSON parser for FHIR bundles using Utf8JsonReader.
/// Enables zero-buffering, incremental parsing of large bundles directly from HTTP request streams.
/// </summary>
public class StreamingBundleParser
{
    private readonly ILogger<StreamingBundleParser> _logger;
    private readonly int _maxTokenBytes;
    private const int BufferSize = 8192; // 8KB chunks
    private const int MaxPooledBufferSize = 1024 * 1024;
    public const int DefaultMaxTokenBytes = 64 * 1024 * 1024;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamingBundleParser"/> class.
    /// </summary>
    public StreamingBundleParser(ILogger<StreamingBundleParser> logger)
        : this(logger, DefaultMaxTokenBytes)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamingBundleParser"/> class with a maximum JSON token size.
    /// </summary>
    /// <param name="logger">Logger for parser diagnostics.</param>
    /// <param name="maxTokenBytes">Maximum number of bytes allowed for a JSON token.</param>
    public StreamingBundleParser(ILogger<StreamingBundleParser> logger, int maxTokenBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTokenBytes, BufferSize);

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxTokenBytes = maxTokenBytes;
    }

    /// <summary>
    /// Parses a FHIR bundle from a stream incrementally, returning metadata immediately and entries via streaming.
    /// Uses Utf8JsonReader for zero-copy parsing with ArrayPool for buffer management.
    /// The rented buffer is released when entry enumeration completes or faults; if entries are not enumerated,
    /// normal garbage collection reclaims the context and buffer.
    /// </summary>
    /// <param name="bundleStream">The input stream containing the FHIR bundle JSON.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A context containing bundle metadata and a streaming enumerable of entries.</returns>
    public async Task<StreamingBundleContext> ParseStreamAsync(
        Stream bundleStream,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bundleStream);

        _logger.LogDebug("Starting streaming bundle parse");

        // Create parser state to be shared between header parsing and entry streaming
        var parserState = new BundleParserState();

        // Create a buffered stream wrapper that allows us to peek at the header
        // and then continue streaming entries from the same position
        var sharedBuffer = new SharedStreamBuffer(bundleStream, BufferSize, _maxTokenBytes);

        try
        {
            // Parse bundle header (resourceType, type, links) until we reach the entry array
            await ParseBundleHeaderAsync(sharedBuffer, parserState, ct);

            _logger.LogDebug(
                "Bundle header parsed: resourceType={ResourceType}, type={Type}, links={LinkCount}, issues={IssueCount}",
                parserState.BundleResourceType ?? "(null)",
                parserState.BundleType ?? "(null)",
                parserState.Links.Count,
                parserState.ParsingIssues.Count);

            // Create streaming enumerable for entries
            var entries = ParseEntriesInternalAsync(sharedBuffer, parserState, ct);

            // Return context with metadata + streaming entries
            return new StreamingBundleContext
            {
                ResourceType = parserState.BundleResourceType ?? "Unknown",
                BundleType = parserState.BundleType,
                Links = parserState.Links,
                ParsingIssues = parserState.ParsingIssues,
                Entries = entries
            };
        }
        catch
        {
            sharedBuffer.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Parses the bundle header (resourceType, type, links) until reaching the entry array or end of bundle.
    /// </summary>
    private async Task ParseBundleHeaderAsync(SharedStreamBuffer buffer, BundleParserState parserState, CancellationToken ct)
    {
        var state = new JsonReaderState();

        // Read chunks until we've parsed the header (reached entry array or end)
        while (!parserState.IsInEntryArray)
        {
            if (!buffer.HasUnconsumedBytes)
            {
                if (buffer.IsComplete)
                {
                    break;
                }

                await buffer.ReadNextChunkAsync(ct);
            }

            if (!buffer.HasUnconsumedBytes)
            {
                break;
            }

            var reader = new Utf8JsonReader(
                buffer.GetReadableSpan(),
                isFinalBlock: buffer.IsComplete,
                state);

            // Process tokens for header
            bool foundEntryArray = false;
            while (reader.Read())
            {
                ProcessHeaderToken(ref reader, parserState);

                // Stop once we enter the entry array
                if (parserState.IsInEntryArray)
                {
                    foundEntryArray = true;
                    break;
                }
            }

            // Save state and mark consumed bytes
            state = reader.CurrentState;
            int bytesConsumed = (int)reader.BytesConsumed;

            buffer.MarkBytesConsumed(bytesConsumed);
            buffer.SaveReaderState(state);

            // Break if we've entered the entry array
            if (foundEntryArray)
            {
                break;
            }

            if (!buffer.IsComplete)
            {
                await buffer.ReadNextChunkAsync(ct);
            }
        }
    }

    /// <summary>
    /// Processes a single token during header parsing.
    /// </summary>
    private void ProcessHeaderToken(ref Utf8JsonReader reader, BundleParserState state)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.PropertyName:
                state.CurrentProperty = reader.GetString();
                break;

            case JsonTokenType.StartObject:
                state.IncrementDepth();

                // Check if starting a link object within link array
                // Link array is at depth 2, so link objects are at depth 3
                if (state.InLinkArray && state.Depth == 3)
                {
                    state.EnterLinkObject();
                }
                break;

            case JsonTokenType.EndObject:

                // Check if ending a link object
                // Link objects are at depth 3 (inside link array at depth 2)
                if (state.InLinkObject && state.Depth == 3)
                {
                    state.ExitLinkObject();
                }

                state.DecrementDepth();
                break;

            case JsonTokenType.StartArray:
                state.IncrementDepth();

                if (state.CurrentProperty == "entry" && state.Depth == 2)
                {
                    state.EnterEntryArray();
                }
                else if (state.CurrentProperty == "link" && state.Depth == 2)
                {
                    state.EnterLinkArray();
                }
                break;

            case JsonTokenType.EndArray:

                if (state.InLinkArray && state.Depth == 2)
                {
                    state.ExitLinkArray();
                }

                state.DecrementDepth();
                break;

            case JsonTokenType.String:
                var stringValue = reader.GetString();

                // Capture bundle-level properties
                if (state.Depth == 1 && !state.IsInEntryArray)
                {
                    state.SetBundleProperty(state.CurrentProperty, stringValue);
                }

                // Capture link properties
                // Link object properties are at depth 3 (inside link object at depth 3)
                if (state.InLinkObject && state.Depth == 3)
                {
                    state.SetLinkProperty(state.CurrentProperty, stringValue);
                }
                break;

            // Ignore other token types during header parsing
        }
    }

    /// <summary>
    /// Parses bundle entries from the stream (called after header is parsed).
    /// </summary>
    private async IAsyncEnumerable<BundleEntryContext> ParseEntriesInternalAsync(
        SharedStreamBuffer buffer,
        BundleParserState parserState,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var state = buffer.GetCurrentReaderState();
        int entriesYielded = 0;
        try
        {
            while (!buffer.IsComplete || buffer.HasUnconsumedBytes)
            {
                if (!buffer.HasUnconsumedBytes && !buffer.IsComplete)
                {
                    await buffer.ReadNextChunkAsync(ct);
                }

                if (!buffer.HasUnconsumedBytes && buffer.IsComplete)
                {
                    break;
                }

                var reader = new Utf8JsonReader(
                    buffer.GetReadableSpan(),
                    isFinalBlock: buffer.IsComplete,
                    state);

                var completedEntries = ProcessTokens(ref reader, parserState);

                state = reader.CurrentState;
                buffer.MarkBytesConsumed((int)reader.BytesConsumed);

                foreach (var entry in completedEntries)
                {
                    entriesYielded++;
                    _logger.LogDebug("Entry {Index} complete, yielding", entry.Index);
                    yield return entry;
                }

                if (parserState.EntryArrayClosed)
                {
                    break;
                }

                if (!buffer.IsComplete)
                {
                    await buffer.ReadNextChunkAsync(ct);
                }
            }

            _logger.LogInformation(
                "Streaming bundle parse complete: {EntryCount} entries yielded",
                entriesYielded);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    /// <summary>
    /// Processes all tokens in the current buffer and returns completed entries.
    /// </summary>
    private List<BundleEntryContext> ProcessTokens(ref Utf8JsonReader reader, BundleParserState parserState)
    {
        var completedEntries = new List<BundleEntryContext>();

        while (reader.Read())
        {
            ProcessToken(ref reader, parserState);

            // Collect entry when complete
            if (parserState.IsEntryComplete)
            {
                completedEntries.Add(parserState.CurrentEntry!);
                parserState.ResetEntry();
            }
        }

        return completedEntries;
    }

    /// <summary>
    /// Processes a single JSON token and updates the parser state.
    /// </summary>
    private void ProcessToken(ref Utf8JsonReader reader, BundleParserState state)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.PropertyName:
                state.CurrentProperty = reader.GetString();

                // Check for special properties
                if (state.IsInEntry && state.Depth == 3 && state.CurrentProperty == "request")
                {
                    state.EnterRequest();
                }

                // If inside resource, add property name to JSON immediately
                if (state.InResource)
                {
                    state.AppendCommaIfNeeded();
                    AppendResourceStringToken(ref reader, state);
                    state.AppendResourceToken(":");
                    state.SetPropertyNameProcessed(); // Mark that we just processed a property name
                }

                break;

            case JsonTokenType.StartObject:
                // Check if starting a new bundle entry BEFORE incrementing depth
                // Entry objects are immediate children of the entry array (depth 2)
                if (state.IsInEntryArray && state.Depth == 2)
                {
                    state.IncrementDepth(); // Now at depth 3 (inside entry object)
                    state.StartNewEntry();
                }
                else
                {
                    state.IncrementDepth();
                }

                // Check if starting the "resource" object
                if (state.IsInEntry && state.CurrentProperty == "resource" && !state.InResource)
                {
                    state.EnterResource();
                }
                else if (state.InResource)
                {
                    // Nested object within resource - need comma if this is an array element
                    if (!state.JustProcessedPropertyName)
                    {
                        state.AppendCommaIfNeeded();
                    }
                    state.IncrementResourceDepth();
                    state.AppendResourceToken("{");
                    state.ClearPropertyNameFlag(); // Clear property name flag
                    state.ResetCommaFlag(); // Reset comma for properties inside this object
                }

                break;

            case JsonTokenType.EndObject:

                // Handle end of resource
                if (state.InResource)
                {
                    state.AppendResourceToken("}");
                    if (state.DecrementResourceDepth())
                    {
                        state.ExitResource();
                    }
                    else
                    {
                        // After closing a nested object, the next item needs a comma
                        state.SetCommaNeeded();
                    }
                }

                // Check if we're exiting the request object
                // We entered request at depth 3 (when we saw "request" property), and the request object is at depth 4
                // So when depth is 4 and we're in request, we're exiting the request object
                if (state.InRequest && state.Depth == 4)
                {
                    state.ExitRequest();
                }

                state.DecrementDepth();

                // Check if ending a bundle entry
                // After decrementing, if we're back at depth 2 (entry array level), the entry is complete
                if (state.IsInEntryArray && state.Depth == 2)
                {
                    state.CompleteEntry();
                }

                break;

            case JsonTokenType.StartArray:
                state.IncrementDepth();

                if (state.CurrentProperty == "entry" && state.Depth == 2)
                {
                    state.EnterEntryArray();
                }
                else if (state.InResource)
                {
                    state.AppendResourceToken("[");
                    state.ClearPropertyNameFlag(); // Clear property name flag
                    state.ResetCommaFlag(); // Reset comma for elements inside this array
                }

                break;

            case JsonTokenType.EndArray:

                if (state.InResource)
                {
                    state.AppendResourceToken("]");
                    // After closing an array, the next item needs a comma
                    state.SetCommaNeeded();
                }
                // Check if we're closing the entry array (we're at depth 2, which is the entry array level)
                else if (state.IsInEntryArray && state.Depth == 2)
                {
                    state.CloseEntryArray();
                }

                state.DecrementDepth();
                break;

            case JsonTokenType.String:
                // Capture property values for entry metadata
                if (state.IsInEntry && !state.InResource)
                {
                    state.SetPropertyValue(state.CurrentProperty, reader.GetString());
                }

                // Append to resource JSON if inside resource
                if (state.InResource)
                {
                    // Only add comma for array elements, not for property values
                    if (!state.JustProcessedPropertyName)
                    {
                        state.AppendCommaIfNeeded();
                    }
                    AppendResourceStringToken(ref reader, state);
                    state.ClearPropertyNameFlag(); // Clear the property name flag
                    state.SetCommaNeeded(); // Set flag for next item
                }

                break;

            case JsonTokenType.Number:
                if (state.InResource)
                {
                    // Only add comma for array elements, not for property values
                    if (!state.JustProcessedPropertyName)
                    {
                        state.AppendCommaIfNeeded();
                    }
                    AppendResourceNumberValue(ref reader, state);
                    state.ClearPropertyNameFlag(); // Clear the property name flag
                    state.SetCommaNeeded(); // Set flag for next item
                }

                break;

            case JsonTokenType.True:
                if (state.InResource)
                {
                    // Only add comma for array elements, not for property values
                    if (!state.JustProcessedPropertyName)
                    {
                        state.AppendCommaIfNeeded();
                    }
                    AppendResourceValue(state, "true");
                    state.ClearPropertyNameFlag(); // Clear the property name flag
                    state.SetCommaNeeded(); // Set flag for next item
                }

                break;

            case JsonTokenType.False:
                if (state.InResource)
                {
                    // Only add comma for array elements, not for property values
                    if (!state.JustProcessedPropertyName)
                    {
                        state.AppendCommaIfNeeded();
                    }
                    AppendResourceValue(state, "false");
                    state.ClearPropertyNameFlag(); // Clear the property name flag
                    state.SetCommaNeeded(); // Set flag for next item
                }

                break;

            case JsonTokenType.Null:
                if (state.InResource)
                {
                    // Only add comma for array elements, not for property values
                    if (!state.JustProcessedPropertyName)
                    {
                        state.AppendCommaIfNeeded();
                    }
                    AppendResourceValue(state, "null");
                    state.ClearPropertyNameFlag(); // Clear the property name flag
                    state.SetCommaNeeded(); // Set flag for next item
                }

                break;
        }
    }

    /// <summary>
    /// Appends a raw JSON string token to the resource JSON being built.
    /// </summary>
    private static void AppendResourceStringToken(ref Utf8JsonReader reader, BundleParserState state)
    {
        if (reader.HasValueSequence)
        {
            throw new InvalidOperationException("Streaming bundle parser requires contiguous JSON string tokens.");
        }

        state.AppendResourceToken("\"");
        state.AppendResourceToken(Encoding.UTF8.GetString(reader.ValueSpan));
        state.AppendResourceToken("\"");
    }

    /// <summary>
    /// Appends a number value to the resource JSON being built.
    /// Property name should already be added when PropertyName token was processed.
    /// </summary>
    private void AppendResourceNumberValue(ref Utf8JsonReader reader, BundleParserState state)
    {
        // Get number as string (preserves decimal precision)
        var numberValue = Encoding.UTF8.GetString(reader.ValueSpan);
        state.AppendResourceToken(numberValue);
    }

    /// <summary>
    /// Appends a primitive value (true, false, null) to the resource JSON being built.
    /// Property name should already be added when PropertyName token was processed.
    /// </summary>
    private void AppendResourceValue(BundleParserState state, string value)
    {
        state.AppendResourceToken(value);
    }

    /// <summary>
    /// Helper class that manages a shared buffer for header and entry parsing.
    /// Allows reading from a stream in chunks while maintaining unconsumed bytes.
    /// </summary>
    private class SharedStreamBuffer
    {
        private readonly Stream _stream;
        private readonly int _maxTokenBytes;
        private byte[] _buffer;
        private int _bytesInBuffer;
        private bool _isComplete;
        private bool _isPooled;
        private bool _isDisposed;
        private JsonReaderState _currentReaderState;

        public SharedStreamBuffer(Stream stream, int bufferSize, int maxTokenBytes)
        {
            _stream = stream;
            _buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            _maxTokenBytes = maxTokenBytes;
            _bytesInBuffer = 0;
            _isComplete = false;
            _isPooled = true;
            _currentReaderState = new JsonReaderState();
        }

        public bool IsComplete => _isComplete;

        public bool HasUnconsumedBytes => _bytesInBuffer > 0;

        public ReadOnlySpan<byte> GetReadableSpan() => _buffer.AsSpan(0, _bytesInBuffer);

        public JsonReaderState GetCurrentReaderState() => _currentReaderState;

        public async Task ReadNextChunkAsync(CancellationToken ct)
        {
            EnsureSpaceForMoreData();

            int totalBytesRead = 0;
            int spaceRemaining = _buffer.Length - _bytesInBuffer;

            while (spaceRemaining > 0)
            {
                int bytesRead = await _stream.ReadAsync(
                    _buffer.AsMemory(_bytesInBuffer + totalBytesRead, spaceRemaining),
                    ct);

                if (bytesRead == 0)
                {
                    // End of stream
                    _isComplete = true;
                    break;
                }

                totalBytesRead += bytesRead;
                spaceRemaining -= bytesRead;
            }

            _bytesInBuffer += totalBytesRead;
        }

        public void MarkBytesConsumed(int bytesConsumed)
        {
            int unconsumedBytes = _bytesInBuffer - bytesConsumed;
            if (unconsumedBytes > 0)
            {
                // Move unconsumed bytes to start of buffer
                Buffer.BlockCopy(_buffer, bytesConsumed, _buffer, 0, unconsumedBytes);
            }

            _bytesInBuffer = unconsumedBytes;
        }

        public void SaveReaderState(JsonReaderState state)
        {
            _currentReaderState = state;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            if (_isPooled)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
            }
        }

        private void EnsureSpaceForMoreData()
        {
            if (_bytesInBuffer < _buffer.Length)
            {
                return;
            }

            int newBufferSize = checked(_buffer.Length * 2);
            if (newBufferSize > _maxTokenBytes)
            {
                throw new RequestNotValidException(
                    $"Bundle contains a JSON token larger than the maximum supported size of {_maxTokenBytes} bytes.");
            }

            bool newBufferIsPooled = newBufferSize <= MaxPooledBufferSize;
            byte[] newBuffer = newBufferIsPooled
                ? ArrayPool<byte>.Shared.Rent(newBufferSize)
                : GC.AllocateUninitializedArray<byte>(newBufferSize);

            Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _bytesInBuffer);
            if (_isPooled)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
            }

            _buffer = newBuffer;
            _isPooled = newBufferIsPooled;
        }
    }
}

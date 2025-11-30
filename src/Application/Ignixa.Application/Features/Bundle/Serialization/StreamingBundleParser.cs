// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Bundle.Serialization;

/// <summary>
/// Streaming JSON parser for FHIR bundles using Utf8JsonReader.
/// Enables zero-buffering, incremental parsing of large bundles directly from HTTP request streams.
/// </summary>
public class StreamingBundleParser
{
    private readonly ILogger<StreamingBundleParser> _logger;
    private const int BufferSize = 8192; // 8KB chunks

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamingBundleParser"/> class.
    /// </summary>
    public StreamingBundleParser(ILogger<StreamingBundleParser> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Parses a FHIR bundle from a stream incrementally, returning metadata immediately and entries via streaming.
    /// Uses Utf8JsonReader for zero-copy parsing with ArrayPool for buffer management.
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
        var sharedBuffer = new SharedStreamBuffer(bundleStream, BufferSize);

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

    /// <summary>
    /// Parses the bundle header (resourceType, type, links) until reaching the entry array or end of bundle.
    /// </summary>
    private async Task ParseBundleHeaderAsync(SharedStreamBuffer buffer, BundleParserState parserState, CancellationToken ct)
    {
        var state = new JsonReaderState();

        // Read chunks until we've parsed the header (reached entry array or end)
        while (!parserState.IsInEntryArray && !buffer.IsComplete)
        {
            // Read next chunk if we don't have any unconsumed bytes
            if (!buffer.HasUnconsumedBytes)
            {
                await buffer.ReadNextChunkAsync(ct);

                // If still no data after reading, we're done
                if (!buffer.HasUnconsumedBytes)
                {
                    break;
                }
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
        int noProgressIterations = 0;
        const int MaxNoProgressIterations = 3; // Allow 3 iterations without progress before throwing

        while (!buffer.IsComplete || buffer.HasUnconsumedBytes)
        {
            // Read next chunk if needed (when we have no data OR we need more data to complete a token)
            if (!buffer.HasUnconsumedBytes && !buffer.IsComplete)
            {
                _logger.LogTrace("Reading next chunk for entry parsing (buffer empty)");
                await buffer.ReadNextChunkAsync(ct);
            }

            // If no data and stream is complete, we're done
            if (!buffer.HasUnconsumedBytes && buffer.IsComplete)
            {
                _logger.LogDebug("No more data and stream complete, ending entry parsing");
                break;
            }

            var reader = new Utf8JsonReader(
                buffer.GetReadableSpan(),
                isFinalBlock: buffer.IsComplete,
                state);

            // Process tokens and collect completed entries
            var completedEntries = ProcessTokens(ref reader, parserState);

            // Track progress: made progress if we consumed bytes OR yielded entries
            bool madeProgress = reader.BytesConsumed > 0 || completedEntries.Count > 0;

            if (!madeProgress)
            {
                noProgressIterations++;
                _logger.LogWarning("No progress iteration {Count}/{Max} - BytesConsumed: 0, CompletedEntries: 0, BytesInBuffer: {BufferBytes}, IsComplete: {IsComplete}, HasSpace: {HasSpace}",
                    noProgressIterations, MaxNoProgressIterations, buffer.GetReadableSpan().Length, buffer.IsComplete, buffer.HasSpaceForMoreData());

                // If stream is NOT complete AND we have space for more data, try reading more data
                // This handles the case where a JSON token (like a long string) spans the current buffer boundary
                if (!buffer.IsComplete && buffer.HasSpaceForMoreData())
                {
                    _logger.LogTrace("No progress but can read more data - attempting to read");
                    await buffer.ReadNextChunkAsync(ct);
                    // Continue to next iteration to try parsing again with more data
                    continue;
                }

                // If we've exceeded the threshold, throw an exception
                if (noProgressIterations >= MaxNoProgressIterations)
                {
                    var remaining = buffer.GetReadableSpan();
                    var remainingStr = Encoding.UTF8.GetString(remaining);
                    var errorMessage = $"Parser stuck in infinite loop after {noProgressIterations} iterations with no progress. " +
                                     $"Unconsumed bytes: {remaining.Length}, Content: '{remainingStr}', " +
                                     $"EntriesYielded: {entriesYielded}, EntryArrayClosed: {parserState.EntryArrayClosed}";

                    _logger.LogError("Infinite loop detected: {Message}", errorMessage);
                    throw new InvalidOperationException(errorMessage);
                }

                // If stream is complete and we can't make progress, exit gracefully
                _logger.LogDebug("No progress made and stream complete, ending entry parsing (completed {Count} entries this iteration)",
                    completedEntries.Count);
                // Log remaining bytes for debugging
                if (buffer.HasUnconsumedBytes)
                {
                    var remaining = buffer.GetReadableSpan();
                    var remainingStr = Encoding.UTF8.GetString(remaining);
                    _logger.LogWarning("Exiting with {ByteCount} unconsumed bytes: {RemainingContent}",
                        remaining.Length, remainingStr);
                }
                break;
            }
            else
            {
                // Reset counter when we make progress
                noProgressIterations = 0;
            }

            // Save reader state for next iteration
            state = reader.CurrentState;
            buffer.MarkBytesConsumed((int)reader.BytesConsumed);

            // Yield completed entries
            foreach (var entry in completedEntries)
            {
                entriesYielded++;
                _logger.LogDebug("Entry {Index} complete, yielding", entry.Index);
                yield return entry;
            }

            // If we've closed the entry array, we're done parsing entries
            if (parserState.EntryArrayClosed)
            {
                break;
            }
        }

        _logger.LogInformation(
            "Streaming bundle parse complete: {EntryCount} entries yielded",
            entriesYielded);
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
                    state.AppendResourceToken($"\"{state.CurrentProperty}\":");
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
                var stringValue = reader.GetString();

                // Capture property values for entry metadata
                if (state.IsInEntry && !state.InResource)
                {
                    state.SetPropertyValue(state.CurrentProperty, stringValue);
                }

                // Append to resource JSON if inside resource
                if (state.InResource)
                {
                    // Only add comma for array elements, not for property values
                    if (!state.JustProcessedPropertyName)
                    {
                        state.AppendCommaIfNeeded();
                    }
                    AppendResourceStringValue(state, stringValue);
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
    /// Appends a string value to the resource JSON being built.
    /// Property name should already be added when PropertyName token was processed.
    /// </summary>
    private void AppendResourceStringValue(BundleParserState state, string? value)
    {
        // Escape the string value
        var escapedValue = EscapeJsonString(value ?? string.Empty);
        state.AppendResourceToken($"\"{escapedValue}\"");
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
    /// Escapes a string value for JSON.
    /// </summary>
    private static string EscapeJsonString(string value)
    {
        // Simple implementation - could be optimized
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }

    /// <summary>
    /// Helper class that manages a shared buffer for header and entry parsing.
    /// Allows reading from a stream in chunks while maintaining unconsumed bytes.
    /// </summary>
    private class SharedStreamBuffer
    {
        private readonly Stream _stream;
        private readonly byte[] _buffer;
        private int _bytesInBuffer;
        private int _totalBytesRead;
        private bool _isComplete;
        private JsonReaderState _currentReaderState;

        public SharedStreamBuffer(Stream stream, int bufferSize)
        {
            _stream = stream;
            _buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            _bytesInBuffer = 0;
            _totalBytesRead = 0;
            _isComplete = false;
            _currentReaderState = new JsonReaderState();
        }

        public bool IsComplete => _isComplete;

        public bool HasUnconsumedBytes => _bytesInBuffer > 0;

        public bool HasSpaceForMoreData() => _bytesInBuffer < _buffer.Length;

        public ReadOnlySpan<byte> GetReadableSpan() => _buffer.AsSpan(0, _bytesInBuffer);

        public JsonReaderState GetCurrentReaderState() => _currentReaderState;

        public async Task ReadNextChunkAsync(CancellationToken ct)
        {
            // Read next chunk from stream into buffer
            // Keep reading until we fill the buffer or reach end of stream
            // This ensures we don't mark as complete prematurely when tokens span chunk boundaries
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

            _totalBytesRead += totalBytesRead;
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
            ArrayPool<byte>.Shared.Return(_buffer);
        }
    }
}

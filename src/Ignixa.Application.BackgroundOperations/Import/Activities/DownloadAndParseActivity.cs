// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using DurableTask.Core;
using Ignixa.Domain.Models;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Import.Activities;

/// <summary>
/// Downloads NDJSON file from blob storage or local file system and parses into batches.
/// Uses streaming to avoid loading entire file into memory.
/// </summary>
public class DownloadAndParseActivity : AsyncTaskActivity<DownloadAndParseInput, DownloadAndParseOutput>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DownloadAndParseActivity> _logger;

    public DownloadAndParseActivity(
        IHttpClientFactory httpClientFactory,
        ILogger<DownloadAndParseActivity> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<DownloadAndParseOutput> ExecuteAsync(
        TaskContext context,
        DownloadAndParseInput input)
    {
        _logger.LogInformation(
            "Downloading and parsing NDJSON file: {Url} (batch size: {BatchSize})",
            input.FileUrl,
            input.BatchSize);

        var batches = new List<ResourceBatch>();

        try
        {
            // Determine if URL is HTTP or local file path
            var isHttpUrl = input.FileUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                           input.FileUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

            Stream stream;
            if (isHttpUrl)
            {
                // Download from HTTP(S)
                stream = await DownloadFromHttpAsync(input.FileUrl);
            }
            else
            {
                // Read from local file system
                if (!File.Exists(input.FileUrl))
                {
                    throw new FileNotFoundException($"Import file not found: {input.FileUrl}");
                }

#pragma warning disable CA2000 // Stream is disposed by the await using block below
                stream = File.OpenRead(input.FileUrl);
#pragma warning restore CA2000
            }

            await using (stream)
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);

                var currentBatch = new List<string>();
                var lineNumber = 0;

                // Read line by line (NDJSON format)
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    lineNumber++;

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue; // Skip empty lines
                    }

                    // Add to current batch
                    currentBatch.Add(line);

                    // When batch is full, create ResourceBatch and start new batch
                    if (currentBatch.Count >= input.BatchSize)
                    {
                        batches.Add(new ResourceBatch
                        {
                            BatchNumber = batches.Count + 1,
                            Resources = currentBatch.ToList(),
                            StartLine = lineNumber - currentBatch.Count + 1,
                            EndLine = lineNumber
                        });

                        currentBatch.Clear();
                    }
                }

                // Add remaining resources as final batch
                if (currentBatch.Any())
                {
                    batches.Add(new ResourceBatch
                    {
                        BatchNumber = batches.Count + 1,
                        Resources = currentBatch.ToList(),
                        StartLine = lineNumber - currentBatch.Count + 1,
                        EndLine = lineNumber
                    });
                }

                _logger.LogInformation(
                    "Parsed {LineCount} resources into {BatchCount} batches from {Url}",
                    lineNumber,
                    batches.Count,
                    input.FileUrl);

                return new DownloadAndParseOutput
                {
                    Batches = batches,
                    TotalLines = lineNumber
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading/parsing file {Url}", input.FileUrl);
            throw new InvalidOperationException($"Failed to download/parse file {input.FileUrl}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Downloads file from HTTP(S) URL using streaming.
    /// </summary>
    private async Task<Stream> DownloadFromHttpAsync(string url)
    {
#pragma warning disable CA2000 // HttpClient from factory should not be disposed - managed by factory
        var httpClient = _httpClientFactory.CreateClient();
#pragma warning restore CA2000

        try
        {
            // Stream download (don't load entire file into memory)
            var uri = new Uri(url);
            var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStreamAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file from {Url}", url);
            throw new InvalidOperationException($"Failed to download file from {url}: {ex.Message}", ex);
        }
    }
}

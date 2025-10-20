// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Import.Activities;

/// <summary>
/// Validates import files (ETag checks, existence).
/// Runs before download to fail fast if files are invalid.
/// </summary>
public class ValidateFileActivity : AsyncTaskActivity<ValidateFileInput, ValidateFileOutput>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ValidateFileActivity> _logger;

    public ValidateFileActivity(
        IHttpClientFactory httpClientFactory,
        ILogger<ValidateFileActivity> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<ValidateFileOutput> ExecuteAsync(
        TaskContext context,
        ValidateFileInput input)
    {
        _logger.LogInformation(
            "Validating {FileCount} input files for job {JobId}",
            input.InputFiles.Count,
            input.JobId);

#pragma warning disable CA2000 // HttpClient from factory should not be disposed - managed by factory
        var httpClient = _httpClientFactory.CreateClient();
#pragma warning restore CA2000

        foreach (var file in input.InputFiles)
        {
            try
            {
                // Determine if URL is HTTP or local file path
                var isHttpUrl = file.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                               file.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

                if (isHttpUrl)
                {
                    // Send HEAD request to check file existence and ETag
                    var uri = new Uri(file.Url);
                    using var request = new HttpRequestMessage(HttpMethod.Head, uri);
                    var response = await httpClient.SendAsync(request);

                    if (!response.IsSuccessStatusCode)
                    {
                        return new ValidateFileOutput
                        {
                            IsValid = false,
                            ErrorMessage = $"File not found or inaccessible: {file.Url} (status: {response.StatusCode})"
                        };
                    }

                    // Check ETag if provided
                    if (!string.IsNullOrEmpty(file.ETag))
                    {
                        var actualETag = response.Headers.ETag?.Tag?.Trim('"');
                        if (actualETag != file.ETag)
                        {
                            return new ValidateFileOutput
                            {
                                IsValid = false,
                                ErrorMessage = $"ETag mismatch for {file.Url}. Expected: {file.ETag}, Actual: {actualETag}"
                            };
                        }
                    }
                }
                else
                {
                    // Local file - check existence
                    if (!File.Exists(file.Url))
                    {
                        return new ValidateFileOutput
                        {
                            IsValid = false,
                            ErrorMessage = $"Local file not found: {file.Url}"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating file {Url}", file.Url);
                return new ValidateFileOutput
                {
                    IsValid = false,
                    ErrorMessage = $"Error validating file {file.Url}: {ex.Message}"
                };
            }
        }

        _logger.LogInformation("All files validated successfully for job {JobId}", input.JobId);

        return new ValidateFileOutput
        {
            IsValid = true
        };
    }
}

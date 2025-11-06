// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Import.Activities;

/// <summary>
/// Validates import files (existence checks) via the blob storage abstraction.
/// Runs before download to fail fast if files are invalid.
/// </summary>
public class ValidateFileActivity : AsyncTaskActivity<ValidateFileInput, ValidateFileOutput>
{
    private readonly IBlobStorageClient _blobStorageClient;
    private readonly ILogger<ValidateFileActivity> _logger;

    public ValidateFileActivity(
        IBlobStorageClient blobStorageClient,
        ILogger<ValidateFileActivity> logger)
    {
        _blobStorageClient = blobStorageClient ?? throw new ArgumentNullException(nameof(blobStorageClient));
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

        foreach (var file in input.InputFiles)
        {
            try
            {
                // Extract blob name from URL (supports both full URLs and relative paths)
                // All file operations go through blob abstraction
                var exists = await _blobStorageClient.BlobExistsAsync(file.Url);

                if (!exists)
                {
                    return new ValidateFileOutput
                    {
                        IsValid = false,
                        ErrorMessage = $"File not found in blob storage: {file.Url}"
                    };
                }

                _logger.LogDebug("File validated in blob storage: {BlobName}", file.Url);
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

    /// <summary>
    /// Extracts the blob name from a URL or path.
    /// Supports both:
    /// - Full blob storage URLs: http://account.blob.core.windows.net/container/blob/name
    /// - Relative paths: container/blob/name or just blob/name
    /// </summary>
    private static string ExtractBlobName(string urlOrPath)
    {
        try
        {
            // If it's a URL, extract the path component
            if (urlOrPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                urlOrPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(urlOrPath);
                var pathWithLeadingSlash = uri.AbsolutePath;
                // Format: /account/container/blobname or /container/blobname
                var pathParts = pathWithLeadingSlash.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

                if (pathParts.Length < 2)
                {
                    return string.Empty;
                }

                // Return everything after the first component (container name)
                // This handles both: account/container/blob and container/blob cases
                return string.Join("/", pathParts.Skip(1));
            }

            // It's already a relative path, return as-is
            return urlOrPath;
        }
        catch
        {
            return string.Empty;
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using Ignixa.PackageManagement.Models;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>Optional verified-artifact cache. The host must own and protect this directory.</summary>
/// <remarks>
/// Entries are opaque hashes of source ID, exact name/version and expected digest, never installed state.
/// Only the acquirer can publish after verification. Reads always undergo its active bounds, digest and
/// strict manifest checks. Cache I/O errors are permanent and explicit, not download fallbacks.
/// </remarks>
public sealed class VerifiedPackageCache
{
    private readonly string _directory;

    public VerifiedPackageCache(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    internal async Task<byte[]?> ReadAsync(
        NpmPackageSourcePolicy policy, NpmPackageIdentity identity, NpmPackageIntegrity integrity,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_directory);
            string path = ArtifactPath(policy, identity, integrity);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new PackageAcquisitionException(PackageAcquisitionError.CacheFailure);
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await BoundedPackageRead.ReadAsync(stream, policy.ExtractionLimits.MaxCompressedBytes,
                PackageAcquisitionError.CompressedSizeLimit, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PackageAcquisitionException(PackageAcquisitionError.CacheFailure);
        }
    }

    internal async Task PublishAsync(
        NpmPackageSourcePolicy policy, NpmPackageIdentity identity, NpmPackageIntegrity integrity,
        byte[] verifiedBytes, Action checkBudget, CancellationToken cancellationToken)
    {
        string staging = Path.Combine(_directory, $"{Guid.NewGuid():N}.partial");
        bool ownsStaging = false;
        Exception? primaryFailure = null;
        try
        {
            try
            {
                checkBudget();
#pragma warning disable CA2000 // WriteStagingAsync owns disposal, including exceptional write/flush paths.
                var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 65536, FileOptions.Asynchronous);
#pragma warning restore CA2000
                ownsStaging = true;
                await WriteStagingAsync(stream, verifiedBytes, cancellationToken).ConfigureAwait(false);

                checkBudget();
                try
                {
                    File.Move(staging, ArtifactPath(policy, identity, integrity));
                    ownsStaging = false;
                }
                catch (IOException exception) when (exception.HResult is
                    unchecked((int)0x80070050) or unchecked((int)0x800700B7) or 17)
                {
                    // Win32 FILE_EXISTS/ALREADY_EXISTS or Unix EEXIST (raw errno): verify the concurrent winner.
                    byte[]? winner = await ReadAsync(policy, identity, integrity, cancellationToken).ConfigureAwait(false);
                    if (winner is null || !winner.AsSpan().SequenceEqual(verifiedBytes))
                    {
                        throw new PackageAcquisitionException(PackageAcquisitionError.CacheFailure);
                    }
                    checkBudget();
                }
            }
            catch (Exception exception)
            {
                primaryFailure = exception;
                throw;
            }
            finally
            {
                if (ownsStaging)
                {
                    try
                    {
                        File.Delete(staging);
                    }
                    catch (Exception exception) when (primaryFailure is not null &&
                        exception is IOException or UnauthorizedAccessException)
                    {
                        primaryFailure.Data[PackageAcquisitionException.CacheCleanupFailureDataKey] = PackageAcquisitionError.CacheFailure;
                        if (primaryFailure is RetryablePackageException retryable)
                        {
                            PackageAcquisitionException.PreserveCleanupFailure(primaryFailure, retryable.Diagnostic);
                        }
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var failure = new PackageAcquisitionException(PackageAcquisitionError.CacheFailure);
            PackageAcquisitionException.PreserveCleanupFailure(exception, failure);
            throw failure;
        }
    }

    internal static async Task WriteStagingAsync(Stream stream, byte[] verifiedBytes, CancellationToken cancellationToken)
    {
        Exception? primaryFailure = null;
        try
        {
            await stream.WriteAsync(verifiedBytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            try
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (primaryFailure is not null &&
                exception is IOException or UnauthorizedAccessException)
            {
                // Buffered disposal can flush again, even after FlushAsync was canceled.
                primaryFailure.Data[PackageAcquisitionException.CacheCleanupFailureDataKey] = PackageAcquisitionError.CacheFailure;
            }
        }
    }

    private string ArtifactPath(NpmPackageSourcePolicy policy, NpmPackageIdentity identity, NpmPackageIntegrity integrity)
    {
        string key = $"{policy.SourceId}\n{identity.Name}\n{identity.Version}\n{integrity.Integrity}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_directory, $"{hash}.tgz");
    }
}

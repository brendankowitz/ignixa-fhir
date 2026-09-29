using Ignixa.PackageManagement.Models;

namespace Ignixa.PackageManagement.Infrastructure;

internal static class BoundedPackageRead
{
    internal static async Task<byte[]> ReadAsync(
        Stream stream, int limit, PackageAcquisitionError error, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        byte[] buffer = new byte[Math.Min(65536, limit)];
        while (true)
        {
            int count = (int)Math.Min(buffer.Length, limit - output.Length + 1);
            int read = await stream.ReadAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (read == 0)
            {
                return output.ToArray();
            }

            if (output.Length + read > limit)
            {
                throw new PackageAcquisitionException(error);
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }
}

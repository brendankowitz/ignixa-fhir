using System.Text.Json;
using Ignixa.PackageManagement.Models;

namespace Ignixa.PackageManagement.Infrastructure;

internal sealed record NpmSelectedVersion(Uri Tarball, NpmPackageIntegrity Integrity)
{
    internal static NpmSelectedVersion Parse(
        byte[] json, NpmPackageIdentity identity, NpmPackageSourcePolicy policy, NpmPackageIntegrity? pin)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = policy.ExtractionLimits.MaxJsonDepth });
            JsonElement root = document.RootElement;
            if (RequiredString(root, "name") != identity.Name || RequiredString(root, "version") != identity.Version)
            {
                throw new PackageAcquisitionException(PackageAcquisitionError.MetadataIdentityMismatch);
            }

            JsonElement dist = Property(root, "dist");
            string tarball = RequiredString(dist, "tarball");
            if (!Uri.TryCreate(tarball, UriKind.Absolute, out Uri? uri) || !NpmPackageAcquirer.IsAuthorized(uri, policy, metadata: false))
            {
                throw new PackageAcquisitionException(PackageAcquisitionError.UntrustedUri);
            }

            JsonElement integrityValue = Property(dist, "integrity");
            NpmPackageIntegrity? integrity = null;
            if (integrityValue.ValueKind != JsonValueKind.Undefined)
            {
                if (integrityValue.ValueKind != JsonValueKind.String)
                {
                    throw new PackageAcquisitionException(PackageAcquisitionError.InvalidIntegrity);
                }
                try
                {
                    integrity = new NpmPackageIntegrity(DecodeString(integrityValue));
                }
                catch (ArgumentException)
                {
                    throw new PackageAcquisitionException(PackageAcquisitionError.InvalidIntegrity);
                }
            }

            if (pin is not null && integrity is not null && pin != integrity)
            {
                throw new PackageAcquisitionException(PackageAcquisitionError.IntegrityConflict);
            }

            return new NpmSelectedVersion(NpmSourceUri.PreserveWireUri(uri), pin ?? integrity ??
                throw new PackageAcquisitionException(PackageAcquisitionError.MissingIntegrity));
        }
        catch (JsonException)
        {
            throw new PackageAcquisitionException(PackageAcquisitionError.InvalidMetadata);
        }
    }

    private static string RequiredString(JsonElement element, string name)
    {
        JsonElement value = Property(element, name);
        return value.ValueKind == JsonValueKind.String
            ? DecodeString(value)
            : throw new PackageAcquisitionException(PackageAcquisitionError.InvalidMetadata);
    }

    private static JsonElement Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new PackageAcquisitionException(PackageAcquisitionError.InvalidMetadata);
        }

        JsonElement result = default;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            string propertyName;
            try
            {
                propertyName = property.Name;
            }
            catch (InvalidOperationException)
            {
                throw new PackageAcquisitionException(PackageAcquisitionError.InvalidMetadata);
            }
            if (propertyName == name)
            {
                if (result.ValueKind != JsonValueKind.Undefined)
                {
                    throw new PackageAcquisitionException(PackageAcquisitionError.InvalidMetadata);
                }
                result = property.Value;
            }
        }
        return result;
    }

    private static string DecodeString(JsonElement value)
    {
        try
        {
            return value.GetString()!;
        }
        catch (InvalidOperationException)
        {
            // The caller already checked String; this is invalid escaped Unicode, not a shape error.
            throw new PackageAcquisitionException(PackageAcquisitionError.InvalidMetadata);
        }
    }
}

namespace Ignixa.Application.Features.Metadata.Segments;

public interface ICapabilityContextAwarePackageFeature
{
    ValueTask<bool> IsAvailableAsync(
        CapabilityContext context,
        CancellationToken cancellationToken);
}

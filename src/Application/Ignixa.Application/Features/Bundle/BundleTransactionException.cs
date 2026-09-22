using Ignixa.Domain.Exceptions;

namespace Ignixa.Application.Features.Bundle;

public sealed class BundleTransactionException(string message, int statusCode) : BadRequestException(message)
{
    public override int StatusCode => statusCode;
}

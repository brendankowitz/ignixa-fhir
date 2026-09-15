namespace Ignixa.DataLayer.SqlServer;

public sealed class LastNUnavailableException : Exception
{
    public LastNUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

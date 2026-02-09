using Microsoft.Extensions.Logging;

namespace Ignixa.Anonymizer
{
    public static class AnonymizerLogging
    {
        public static ILoggerFactory LoggerFactory { get; set; } = new LoggerFactory();
        public static ILogger CreateLogger<T>() => LoggerFactory.CreateLogger<T>();
    }
}

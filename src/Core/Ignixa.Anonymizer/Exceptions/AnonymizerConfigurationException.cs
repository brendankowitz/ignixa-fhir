using System;

namespace Ignixa.Anonymizer.Exceptions
{
    public class AnonymizerConfigurationException : Exception
    {
        public AnonymizerConfigurationException(string message) : base(message)
        {
        }

        public AnonymizerConfigurationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}

using System;

namespace Ignixa.Anonymizer.Exceptions
{
    public class AddCustomProcessorException : Exception
    {
        public AddCustomProcessorException(string message) : base(message)
        {
        }

        public AddCustomProcessorException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}

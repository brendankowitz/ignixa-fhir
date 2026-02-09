using System;

namespace Ignixa.Anonymizer.Exceptions
{
    public class InvalidInputException : Exception
    {
        public InvalidInputException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}

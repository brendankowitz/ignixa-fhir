using System;

namespace Ignixa.Anonymizer.Validation
{
    public class ResourceNotValidException : Exception
    {
        public ResourceNotValidException(string message) : base(message)
        {
        }
    }
}

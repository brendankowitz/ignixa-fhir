using System;

namespace Ignixa.Anonymizer.Exceptions
{
    public class AnonymizerRuleNotApplicableException : AnonymizerConfigurationException
    {
        public AnonymizerRuleNotApplicableException(string message) : base(message)
        {
        }

        public AnonymizerRuleNotApplicableException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}

using System.Threading.Tasks;

namespace Ignixa.Anonymizer
{
    public interface IFhirDataReader<T>
    {
        Task<T> NextAsync();
    }
}

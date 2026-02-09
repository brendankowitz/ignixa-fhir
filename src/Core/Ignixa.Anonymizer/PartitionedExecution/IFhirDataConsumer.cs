using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ignixa.Anonymizer
{
    public interface IFhirDataConsumer<T>
    {
        Task<int> ConsumeAsync(IEnumerable<T> data);

        Task CompleteAsync();
    }
}

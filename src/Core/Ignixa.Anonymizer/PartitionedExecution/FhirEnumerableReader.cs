// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation + Ignixa Contributors
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ignixa.Anonymizer.PartitionedExecution
{
    public class FhirEnumerableReader<T> : IFhirDataReader<T>
    {
        private IEnumerator<T> _enumerator;

        public FhirEnumerableReader(IEnumerable<T> data)
        {
            _enumerator = data.GetEnumerator();
        }

        public Task<T> NextAsync()
        {
            if (_enumerator.MoveNext())
            {
                return Task.FromResult(_enumerator.Current);
            }
            else
            {
                return Task.FromResult<T>(default(T));
            }
        }
    }
}

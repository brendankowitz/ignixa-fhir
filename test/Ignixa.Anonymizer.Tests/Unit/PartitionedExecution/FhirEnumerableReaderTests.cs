// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ignixa.Anonymizer.PartitionedExecution;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.PartitionedExecution
{
    public class FhirEnumerableReaderTests
    {
        [Fact]
        public async Task GivenAFhirEnumerableReader_ReadData_ResultShouldBeReturnedInOrder()
        {
            int end = 987;
            var reader = new FhirEnumerableReader<string>(Enumerable.Range(0, end).Select(i => i.ToString()));

            int i = 0;
            string nextLine = null;
            while ((nextLine = await reader.NextAsync()) != null)
            {
                Assert.Equal(nextLine, i.ToString());
                i++;
            }

            Assert.Equal(i, end);
        }
    }
}

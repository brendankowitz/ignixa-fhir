// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Ignixa.Anonymizer.PartitionedExecution;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.PartitionedExecution
{
    public class FhirStreamConsumerTests
    {
        [Fact]
        public async Task GivenAFhirStreamConsumer_WhenConsumeData_ShouldReadAllDataFromStream()
        {
            using MemoryStream outputStream = new MemoryStream();
            using FhirStreamConsumer consumer = new FhirStreamConsumer(outputStream);

            int count = await consumer.ConsumeAsync(new List<string>() { "abc", "bcd", ""});
            Assert.Equal(3, count);
            
            await consumer.CompleteAsync();

            outputStream.Position = 0;
            using StreamReader reader = new StreamReader(outputStream);
            Assert.Equal("abc", await reader.ReadLineAsync());
            Assert.Equal("bcd", await reader.ReadLineAsync());
            Assert.Equal("", await reader.ReadLineAsync());
            Assert.Null(await reader.ReadLineAsync());
        }
    }
}

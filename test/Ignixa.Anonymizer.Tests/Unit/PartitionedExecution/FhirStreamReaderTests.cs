// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation + Ignixa Contributors
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.PartitionedExecution
{
    public class FhirStreamReaderTests
    {
        [Fact]
        public async Task GivenAFhirStreamReader_WhenLoadData_ShouldLoadAllDataFromStream()
        {
            using MemoryStream inputStream = new MemoryStream();
            using StreamWriter writer = new StreamWriter(inputStream);
            await writer.WriteLineAsync("abc");
            await writer.WriteLineAsync("bcd");
            await writer.WriteLineAsync("");
            writer.Flush();

            inputStream.Position = 0;
            using FhirStreamReader reader = new FhirStreamReader(inputStream);

            Assert.Equal("abc", await reader.NextAsync());
            Assert.Equal("bcd", await reader.NextAsync());
            Assert.Equal("", await reader.NextAsync());
            Assert.Null(await reader.NextAsync());
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Anonymizer.Exceptions;
using Ignixa.Anonymizer.Processors;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.Processors
{
    public class CustomProcessorFactoryUnitTests
    {
        [Fact]
        public void GivenAFhirProcessorFactory_AddingCustomProcessor_GivenMethod_CorrectProcessorWillBeReturned()
        {
            var factory = new CustomProcessorFactory();
            factory.RegisterProcessors(typeof(MaskProcessor), typeof(MockAnonymizerProcessor));
            Assert.Equal(typeof(MaskProcessor), factory.CreateProcessor("mask", JsonNode.Parse("{\"maskedLength\":\"3\"}")?.AsObject()).GetType());
            Assert.Equal(typeof(MockAnonymizerProcessor), factory.CreateProcessor("mockanonymizer", null).GetType());
        }

        [Fact]
        public void GivenAFhirProcessorFactory_AddingBuildInProcessor_ExceptionWillBeThrown()
        {
            var factory = new CustomProcessorFactory();
            Assert.Throws<AddCustomProcessorException>(() => factory.RegisterProcessors(typeof(RedactProcessor)));
        }
    }
}

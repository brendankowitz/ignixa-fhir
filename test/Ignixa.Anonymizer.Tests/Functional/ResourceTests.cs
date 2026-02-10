// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation + Ignixa Contributors
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.IO;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer;
using Ignixa.Anonymizer.Exceptions;
using Xunit;

namespace Ignixa.Anonymizer.FunctionalTests
{
    public class ResourceTests
    {
        private readonly R4CoreSchemaProvider _schema = new();

        [Fact]
        public void GivenAPatientResource_WhenAnonymizing_ThenAnonymizedJsonShouldBeReturned()
        {
            AnonymizerEngine engine = new AnonymizerEngine(Path.Combine("Configurations", "common-config.json"), _schema);
            FunctionalTestUtility.VerifySingleJsonResourceFromFile(engine, ResourceTestsFile("patient-basic.json"), ResourceTestsFile("patient-basic-target.json"));
        }

        [Fact]
        public void GivenAPatientResource_WhenRedactAll_ThenRedactedJsonShouldBeReturned()
        {
            AnonymizerEngine engine = new AnonymizerEngine(Path.Combine("Configurations", "redact-all-config.json"), _schema);
            FunctionalTestUtility.VerifySingleJsonResourceFromFile(engine, ResourceTestsFile("patient-basic.json"), ResourceTestsFile("patient-redact-all-target.json"));
        }

        [Fact]
        public void GivenAPatientResourceWithSpecialContents_WhenAnonymizing_ThenAnonymizedJsonShouldBeReturned()
        {
            AnonymizerEngine engine = new AnonymizerEngine(Path.Combine("Configurations", "common-config.json"), _schema);
            FunctionalTestUtility.VerifySingleJsonResourceFromFile(engine, ResourceTestsFile("patient-special-content.json"), ResourceTestsFile("patient-special-content-target.json"));
        }

        [Fact(Skip = "Ignixa SDK bug: _birthDate without birthDate produces empty InstanceType on all children - https://github.com/brendankowitz/ignixa-fhir/issues/216")]
        public void GivenAPatientResourceWithNullDatetime_WhenAnonymizing_ThenAnonymizedJsonShouldBeReturned()
        {
            AnonymizerEngine engine = new AnonymizerEngine(Path.Combine("Configurations", "common-config.json"), _schema);
            FunctionalTestUtility.VerifySingleJsonResourceFromFile(engine, ResourceTestsFile("patient-null-date.json"), ResourceTestsFile("patient-null-date-target.json"));
        }

        [Fact]
        public void GivenAPatientResource_WhenAnonymizingWithNoPartialRedactConfig_ThenAnonymizedJsonShouldBeReturned()
        {
            AnonymizerEngine engine = new AnonymizerEngine(Path.Combine("Configurations", "common-no-partial-config.json"), _schema);
            FunctionalTestUtility.VerifySingleJsonResourceFromFile(engine, ResourceTestsFile("patient-no-partial.json"), ResourceTestsFile("patient-no-partial-target.json"));
        }

        [Fact]
        public void GivenAPatientResource_WhenAnonymizingWithPrimitiveSubstituteConfig_ThenAnonymizedJsonShouldBeReturned()
        {
            AnonymizerEngine engine = new AnonymizerEngine(Path.Combine("Configurations", "substitute-primitive.json"), _schema);
            FunctionalTestUtility.VerifySingleJsonResourceFromFile(engine, ResourceTestsFile("patient-substitute-primitive.json"), ResourceTestsFile("patient-substitute-primitive-target.json"));
        }

        [Fact]
        public void GivenAPatientResource_WhenAnonymizingWithComplexSubstituteConfig_ThenAnonymizedJsonShouldBeReturned()
        {
            AnonymizerEngine engine = new AnonymizerEngine(Path.Combine("Configurations", "substitute-complex.json"), _schema);
            FunctionalTestUtility.VerifySingleJsonResourceFromFile(engine, ResourceTestsFile("patient-substitute-complex.json"), ResourceTestsFile("patient-substitute-complex-target.json"));
        }

        [Fact]
        public void GivenAPatientResource_WhenAnonymizingWithConflictRulesSubstituteConfig_ThenAnonymizedJsonShouldBeReturned()
        {
            AnonymizerEngine engine = new AnonymizerEngine(Path.Combine("Configurations", "substitute-conflict-rules.json"), _schema);
            FunctionalTestUtility.VerifySingleJsonResourceFromFile(engine, ResourceTestsFile("patient-substitute-conflict-rules.json"), ResourceTestsFile("patient-substitute-conflict-rules-target.json"));
        }

        [Fact]
        public void GivenAPatientResource_WhenAnonymizingWithGeneralizeConfig_ThenAnonymizedJsonShouldBeReturned()
        {
            AnonymizerEngine engine = new AnonymizerEngine(Path.Combine("Configurations", "generalize-patient-config.json"), _schema);
            FunctionalTestUtility.VerifySingleJsonResourceFromFile(engine, ResourceTestsFile("patient-generalize.json"), ResourceTestsFile("patient-generalize-target.json"));
        }

        [Fact]
        public void GivenAPatientResource_WhenAnonymizingWithMultipleSubstituteConfig_ThenAnonymizedJsonShouldBeReturned()
        {
            // Child node is substituted first
            AnonymizerEngine engine = new AnonymizerEngine(Path.Combine("Configurations", "substitute-multiple.json"), _schema);
            FunctionalTestUtility.VerifySingleJsonResourceFromFile(engine, ResourceTestsFile("patient-substitute-multiple.json"), ResourceTestsFile("patient-substitute-multiple-target.json"));
            // Parent node is substituted first
            engine = new AnonymizerEngine(Path.Combine("Configurations", "substitute-multiple-2.json"), _schema);
            FunctionalTestUtility.VerifySingleJsonResourceFromFile(engine, ResourceTestsFile("patient-substitute-multiple-2.json"), ResourceTestsFile("patient-substitute-multiple-2-target.json"));
        }

        [Fact]
        public void GivenAPatientResource_WhenAnonymizingWithProcessingError_IfSkip_EmptyResultWillBeReturned()
        {
            AnonymizerEngine engine = new AnonymizerEngine(Path.Combine("Configurations", "configuration-skip-processing-error.json"), _schema);
            FunctionalTestUtility.VerifySingleJsonResourceFromFile(engine, ResourceTestsFile("patient-basic.json"), ResourceTestsFile("patient-empty.json"));
        }

        [Fact]
        public void GivenAPatientResource_WhenAnonymizingWithProcessingError_IfRaise_ExceptionWillBeThrown()
        {
            AnonymizerEngine engine = new AnonymizerEngine(Path.Combine("Configurations", "configuration-raise-processing-error.json"), _schema);
            string testContent = File.ReadAllText(ResourceTestsFile("patient-basic.json"));
            Assert.Throws<AnonymizerProcessingException>(() => engine.AnonymizeJson(testContent));
        }

        [Fact]
        public void GivenAPatientResource_WhenAnonymizingWithProcessingError_IfParameterNotGiven_ExceptionWillBeThrown()
        {
            AnonymizerEngine engine = new AnonymizerEngine(Path.Combine("Configurations", "configuration-without-processing-error.json"), _schema);
            string testContent = File.ReadAllText(ResourceTestsFile("patient-basic.json"));
            Assert.Throws<AnonymizerProcessingException>(() => engine.AnonymizeJson(testContent));
        }

        private string ResourceTestsFile(string fileName)
        {
            return Path.Combine("TestResources", fileName);
        }
    }
}

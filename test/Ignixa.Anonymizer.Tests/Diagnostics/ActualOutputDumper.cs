// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation + Ignixa Contributors
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer;
using Xunit;
using Xunit.Abstractions;

namespace Ignixa.Anonymizer.FunctionalTests
{
    public class ActualOutputDumper
    {
        private readonly ITestOutputHelper _output;
        private readonly R4CoreSchemaProvider _schema = new();

        public ActualOutputDumper(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void DumpPatientNullDate_CommonConfig()
        {
            var engine = new AnonymizerEngine(Path.Combine("Configurations", "common-config.json"), _schema);
            var actual = FunctionalTestUtility.GetActualOutput(engine, Path.Combine("TestResources", "patient-null-date.json"));
            _output.WriteLine("=== ACTUAL OUTPUT: Patient-null-date with common-config.json ===");
            _output.WriteLine(actual);
            _output.WriteLine("=== END OUTPUT ===");
        }

        [Fact]
        public void DumpPatientGeneralize_GeneralizeConfig()
        {
            var engine = new AnonymizerEngine(Path.Combine("Configurations", "generalize-patient-config.json"), _schema);
            var actual = FunctionalTestUtility.GetActualOutput(engine, Path.Combine("TestResources", "patient-generalize.json"));
            _output.WriteLine("=== ACTUAL OUTPUT: Patient-generalize with generalize-patient-config.json ===");
            _output.WriteLine(actual);
            _output.WriteLine("=== END OUTPUT ===");
        }

        [Fact]
        public void DumpPatientSubstituteComplex_SubstituteComplexConfig()
        {
            var engine = new AnonymizerEngine(Path.Combine("Configurations", "substitute-complex.json"), _schema);
            var actual = FunctionalTestUtility.GetActualOutput(engine, Path.Combine("TestResources", "patient-substitute-complex.json"));
            _output.WriteLine("=== ACTUAL OUTPUT: Patient-substitute-complex with substitute-complex.json ===");
            _output.WriteLine(actual);
            _output.WriteLine("=== END OUTPUT ===");
        }

        [Fact]
        public void DumpPatientSubstituteConflictRules_SubstituteConflictRulesConfig()
        {
            var engine = new AnonymizerEngine(Path.Combine("Configurations", "substitute-conflict-rules.json"), _schema);
            var actual = FunctionalTestUtility.GetActualOutput(engine, Path.Combine("TestResources", "patient-substitute-conflict-rules.json"));
            _output.WriteLine("=== ACTUAL OUTPUT: Patient-substitute-conflict-rules with substitute-conflict-rules.json ===");
            _output.WriteLine(actual);
            _output.WriteLine("=== END OUTPUT ===");
        }

        [Fact]
        public void DumpPatientSubstituteMultiple_SubstituteMultipleConfig()
        {
            var engine = new AnonymizerEngine(Path.Combine("Configurations", "substitute-multiple.json"), _schema);
            var actual = FunctionalTestUtility.GetActualOutput(engine, Path.Combine("TestResources", "patient-substitute-multiple.json"));
            _output.WriteLine("=== ACTUAL OUTPUT: Patient-substitute-multiple with substitute-multiple.json ===");
            _output.WriteLine(actual);
            _output.WriteLine("=== END OUTPUT ===");
        }

        [Fact]
        public void DumpPatientSubstituteMultiple2_SubstituteMultiple2Config()
        {
            var engine = new AnonymizerEngine(Path.Combine("Configurations", "substitute-multiple-2.json"), _schema);
            var actual = FunctionalTestUtility.GetActualOutput(engine, Path.Combine("TestResources", "patient-substitute-multiple-2.json"));
            _output.WriteLine("=== ACTUAL OUTPUT: Patient-substitute-multiple-2 with substitute-multiple-2.json ===");
            _output.WriteLine(actual);
            _output.WriteLine("=== END OUTPUT ===");
        }

        [Fact]
        public void DumpBundleBasic_CommonConfig()
        {
            var engine = new AnonymizerEngine(Path.Combine("Configurations", "common-config.json"), _schema);
            var actual = FunctionalTestUtility.GetActualOutput(engine, Path.Combine("TestResources", "bundle-basic.json"));
            _output.WriteLine("=== ACTUAL OUTPUT: Bundle-basic with common-config.json ===");
            _output.WriteLine(actual);
            _output.WriteLine("=== END OUTPUT ===");
        }

        [Fact]
        public void DumpBundleSubstitute_SubstituteMultipleConfig()
        {
            var engine = new AnonymizerEngine(Path.Combine("Configurations", "substitute-multiple.json"), _schema);
            var actual = FunctionalTestUtility.GetActualOutput(engine, Path.Combine("TestResources", "bundle-substitute.json"));
            _output.WriteLine("=== ACTUAL OUTPUT: Bundle-substitute with substitute-multiple.json ===");
            _output.WriteLine(actual);
            _output.WriteLine("=== END OUTPUT ===");
        }

        [Fact]
        public void DumpContainedBasic_CommonConfig()
        {
            var engine = new AnonymizerEngine(Path.Combine("Configurations", "common-config.json"), _schema);
            var actual = FunctionalTestUtility.GetActualOutput(engine, Path.Combine("TestResources", "contained-basic.json"));
            _output.WriteLine("=== ACTUAL OUTPUT: Contained-basic with common-config.json ===");
            _output.WriteLine(actual);
            _output.WriteLine("=== END OUTPUT ===");
        }

        [Fact]
        public void DumpContainedSubstitute_SubstituteMultipleConfig()
        {
            var engine = new AnonymizerEngine(Path.Combine("Configurations", "substitute-multiple.json"), _schema);
            var actual = FunctionalTestUtility.GetActualOutput(engine, Path.Combine("TestResources", "contained-substitute.json"));
            _output.WriteLine("=== ACTUAL OUTPUT: Contained-substitute with substitute-multiple.json ===");
            _output.WriteLine(actual);
            _output.WriteLine("=== END OUTPUT ===");
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation + Ignixa Contributors
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.IO;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer;
using Xunit;

namespace Ignixa.Anonymizer.FunctionalTests
{
    /// <summary>
    /// Helper to regenerate target files after breaking changes.
    /// Run these tests once, then copy the output files to replace the old targets.
    /// </summary>
    public class RegenerateTargets
    {
        private readonly R4CoreSchemaProvider _schema = new();

        private void RegenerateTarget(string configFile, string sourceFile, string targetFile)
        {
            var engine = new AnonymizerEngine(configFile, _schema);
            var source = Path.Combine("TestResources", sourceFile);
            var target = Path.Combine("TestResources", targetFile);

            var sourceContent = File.ReadAllText(source);
            var anonymized = engine.AnonymizeJson(sourceContent);

            // Write to a temp file so we can inspect before overwriting
            var tempFile = target + ".new";
            File.WriteAllText(tempFile, anonymized);

            Console.WriteLine($"Generated: {tempFile}");
            Console.WriteLine($"To apply: copy /Y \"{tempFile}\" \"{target}\"");
        }

        [Fact(Skip = "Manual regeneration tool - run when needed")]
        public void RegenerateServiceRequestTarget()
        {
            RegenerateTarget(
                Path.Combine("Functional", "r4-configuration-sample.json"),
                Path.Combine("R4OnlyResource", "ServiceRequest.json"),
                Path.Combine("R4OnlyResource", "ServiceRequest-target.json"));
        }

        [Fact(Skip = "Manual regeneration tool - run when needed")]
        public void RegenerateProcessRequestTarget()
        {
            RegenerateTarget(
                Path.Combine("Functional", "stu3-configuration-sample.json"),
                Path.Combine("Stu3OnlyResource", "ProcessRequest.json"),
                Path.Combine("Stu3OnlyResource", "ProcessRequest-target.json"));
        }

        [Fact(Skip = "Manual regeneration tool - run when needed")]
        public void RegenerateProcessResponseTarget()
        {
            RegenerateTarget(
                Path.Combine("Functional", "stu3-configuration-sample.json"),
                Path.Combine("Stu3OnlyResource", "ProcessResponse.json"),
                Path.Combine("Stu3OnlyResource", "ProcessResponse-target.json"));
        }

        [Fact(Skip = "Manual regeneration tool - run when needed")]
        public void RegenerateBundleBasicTarget()
        {
            RegenerateTarget(
                Path.Combine("Configurations", "common-config.json"),
                "bundle-basic.json",
                "bundle-basic-target.json");
        }

        [Fact(Skip = "Manual regeneration tool - run when needed")]
        public void RegenerateContainedInBundleTarget()
        {
            RegenerateTarget(
                Path.Combine("Configurations", "common-config.json"),
                "contained-in-bundle.json",
                "contained-in-bundle-target.json");
        }
    }
}

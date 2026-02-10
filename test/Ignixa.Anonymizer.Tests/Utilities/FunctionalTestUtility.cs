// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation + Ignixa Contributors
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Anonymizer;
using Xunit;

namespace Ignixa.Anonymizer.FunctionalTests
{
    public static class FunctionalTestUtility
    {
        public static void VerifySingleJsonResourceFromFile(AnonymizerEngine engine, string testFile, string targetFile)
        {
            Console.WriteLine($"VerifySingleJsonResourceFromFile. TestFile: {testFile}, TargetFile: {targetFile}");
            string testContent = File.ReadAllText(testFile);
            string resultAfterAnonymize = engine.AnonymizeJson(testContent);
            string standardizedResult = Standardize(resultAfterAnonymize);

            // Support auto-updating target files for breaking changes
            // Set environment variable: UPDATE_TARGETS=1
            var updateTargets = Environment.GetEnvironmentVariable("UPDATE_TARGETS");
            if (!string.IsNullOrEmpty(updateTargets) && updateTargets == "1")
            {
                var newFile = targetFile + ".new";
                File.WriteAllText(newFile, standardizedResult);
                Console.WriteLine($"Generated new target file: {newFile}");
                Console.WriteLine($"To apply: copy /Y \"{newFile}\" \"{targetFile}\"");
                return;
            }

            string targetContent = File.ReadAllText(targetFile);
            Assert.Equal(Standardize(targetContent), standardizedResult);
        }

        public static string GetActualOutput(AnonymizerEngine engine, string testFile)
        {
            string testContent = File.ReadAllText(testFile);
            string resultAfterAnonymize = engine.AnonymizeJson(testContent);
            return Standardize(resultAfterAnonymize);
        }

        private static string Standardize(string jsonContent)
        {
            // Parse and re-serialize to normalize formatting
            var node = JsonNode.Parse(jsonContent);
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            return node.ToJsonString(options);
        }
    }
}

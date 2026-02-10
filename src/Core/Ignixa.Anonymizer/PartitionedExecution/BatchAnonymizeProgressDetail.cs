// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation + Ignixa Contributors
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
namespace Ignixa.Anonymizer.PartitionedExecution
{
    public class BatchAnonymizeProgressDetail
    {
        public int CurrentThreadId { get; set; }
        
        // The number of anonymization completed resources.
        public int ProcessCompleted { get; set; }
        
        // The number of skipped resources when skipping AnonymizerProcessingException enabled.
        public int ProcessSkipped { get; set; }
        
        // Todo : this property will be removed since exception will be thrown once consuming failed.
        public int ConsumeCompleted { get; set; }
    }
}

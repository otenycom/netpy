namespace Odoo.Core
{
    /// <summary>
    /// Non-generic base interface for values handlers.
    /// Enables runtime dispatch without knowing the concrete values type.
    ///
    /// This interface is used by BaseModel.Write_Base and BaseModel.Create_Base
    /// to apply values generically, without needing model-specific code.
    /// </summary>
    public interface IRecordValuesHandler
    {
        /// <summary>
        /// Apply values to cache for a single record.
        /// </summary>
        void ApplyToCache(
            IRecordValues values,
            IColumnarCache cache,
            ModelHandle model,
            RecordId recordId
        );

        /// <summary>
        /// Mark set fields as dirty.
        /// </summary>
        void MarkDirty(
            IRecordValues values,
            IColumnarCache cache,
            ModelHandle model,
            RecordId recordId
        );

        /// <summary>
        /// Trigger field modification events for computed field recomputation.
        /// </summary>
        void TriggerModified(
            IRecordValues values,
            OdooEnvironment env,
            ModelHandle model,
            RecordId recordId
        );

        /// <summary>
        /// Convert dictionary to typed values.
        /// </summary>
        IRecordValues FromDictionary(Dictionary<string, object?> dict);
    }

    /// <summary>
    /// Handler interface for processing typed values without reflection.
    /// Generated for each model to enable fast field iteration.
    ///
    /// The handler converts typed values to cache operations using generated code
    /// with direct IsSet checks - no reflection, no string parsing.
    ///
    /// Generated handlers implement both IRecordValuesHandler (non-generic)
    /// and IRecordValuesHandler&lt;TValues&gt; (typed) for maximum flexibility.
    /// </summary>
    /// <typeparam name="TValues">The values type to handle</typeparam>
    public interface IRecordValuesHandler<TValues> : IRecordValuesHandler
        where TValues : IRecordValues
    {
        /// <summary>
        /// Apply set fields to cache for a single record.
        /// Generated implementation uses direct IsSet checks - no reflection.
        /// </summary>
        /// <param name="values">The values to apply</param>
        /// <param name="cache">The cache to write to</param>
        /// <param name="model">The model handle</param>
        /// <param name="recordId">The record ID</param>
        void ApplyToCache(
            TValues values,
            IColumnarCache cache,
            ModelHandle model,
            RecordId recordId
        );

        /// <summary>
        /// Apply different values to multiple records (batch create).
        /// Each values instance maps to its corresponding recordId.
        /// </summary>
        /// <param name="valuesCollection">Collection of values to apply</param>
        /// <param name="cache">The cache to write to</param>
        /// <param name="model">The model handle</param>
        /// <param name="recordIds">Array of record IDs (must match values count)</param>
        void ApplyToCacheBatch(
            IEnumerable<TValues> valuesCollection,
            IColumnarCache cache,
            ModelHandle model,
            RecordId[] recordIds
        );

        /// <summary>
        /// Apply same values to multiple records (bulk write).
        /// Odoo pattern: records.write(vals) applies vals to all records.
        /// </summary>
        /// <param name="values">The values to apply to all records</param>
        /// <param name="cache">The cache to write to</param>
        /// <param name="model">The model handle</param>
        /// <param name="recordIds">Array of record IDs to update</param>
        void ApplyToCacheBulk(
            TValues values,
            IColumnarCache cache,
            ModelHandle model,
            RecordId[] recordIds
        );

        /// <summary>
        /// Invoke triggers for modified fields (computed field dependencies).
        /// Called after applying values to trigger recomputation of dependent fields.
        /// </summary>
        /// <param name="values">The values that were applied</param>
        /// <param name="env">The environment for triggering Modified</param>
        /// <param name="model">The model handle</param>
        /// <param name="recordId">The record ID that was modified</param>
        void TriggerModified(
            TValues values,
            OdooEnvironment env,
            ModelHandle model,
            RecordId recordId
        );

        /// <summary>
        /// Invoke triggers for batch of records with same field changes.
        /// More efficient than calling TriggerModified for each record.
        /// </summary>
        /// <param name="values">The values that were applied (determines which fields trigger)</param>
        /// <param name="env">The environment for triggering Modified</param>
        /// <param name="model">The model handle</param>
        /// <param name="recordIds">Array of record IDs that were modified</param>
        void TriggerModifiedBatch(
            TValues values,
            OdooEnvironment env,
            ModelHandle model,
            RecordId[] recordIds
        );
    }
}

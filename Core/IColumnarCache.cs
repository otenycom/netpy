using System;
using System.Collections.Generic;

namespace Odoo.Core
{
    /// <summary>
    /// Columnar cache interface supporting both batch and single-record access.
    /// Optimized for Data-Oriented Design with contiguous memory layout.
    /// <para>
    /// <b>THREAD SAFETY:</b> Implementations are NOT expected to be thread-safe.
    /// The cache is designed to be owned by a single <see cref="IEnvironment"/> context
    /// and accessed by a single thread (or sequentially).
    /// </para>
    /// </summary>
    public interface IColumnarCache
    {
        // --- Batch Operations (Primary Path) ---

        /// <summary>
        /// Get a read-only span of values for a specific field across multiple records.
        /// This is the primary batch access method for high-performance iteration.
        /// </summary>
        /// <typeparam name="T">The field value type</typeparam>
        /// <param name="model">Model handle (integer token)</param>
        /// <param name="ids">Array of record IDs to fetch</param>
        /// <param name="field">Field handle (integer token)</param>
        /// <returns>Read-only span containing values for all requested IDs</returns>
        ReadOnlySpan<T> GetColumnSpan<T>(ModelHandle model, int[] ids, FieldHandle field);

        /// <summary>
        /// Set values for a specific field across multiple records in batch.
        /// </summary>
        /// <typeparam name="T">The field value type</typeparam>
        /// <param name="model">Model handle</param>
        /// <param name="ids">Array of record IDs</param>
        /// <param name="field">Field handle</param>
        /// <param name="values">Values to set (must match length of ids)</param>
        void SetColumnValues<T>(ModelHandle model, int[] ids, FieldHandle field, ReadOnlySpan<T> values);

        // --- Single Record Operations (Optimized through Columnar Backend) ---

        /// <summary>
        /// Get a single value for a specific record and field.
        /// While this uses the columnar backend, batch operations are preferred for performance.
        /// </summary>
        /// <typeparam name="T">The field value type</typeparam>
        /// <param name="model">Model handle</param>
        /// <param name="id">Record ID</param>
        /// <param name="field">Field handle</param>
        /// <returns>The field value</returns>
        T GetValue<T>(ModelHandle model, int id, FieldHandle field);

        /// <summary>
        /// Set a single value for a specific record and field.
        /// </summary>
        /// <typeparam name="T">The field value type</typeparam>
        /// <param name="model">Model handle</param>
        /// <param name="id">Record ID</param>
        /// <param name="field">Field handle</param>
        /// <param name="value">Value to set</param>
        void SetValue<T>(ModelHandle model, int id, FieldHandle field, T value);

        /// <summary>
        /// Check if a value exists in the cache.
        /// </summary>
        /// <param name="model">Model handle</param>
        /// <param name="id">Record ID</param>
        /// <param name="field">Field handle</param>
        /// <returns>True if the value is cached</returns>
        bool HasValue(ModelHandle model, int id, FieldHandle field);

        // --- Prefetch Operations ---

        /// <summary>
        /// Prefetch multiple fields for multiple records in a single operation.
        /// This is useful for optimizing database access patterns.
        /// </summary>
        /// <param name="model">Model handle</param>
        /// <param name="ids">Record IDs to prefetch</param>
        /// <param name="fields">Fields to prefetch</param>
        void Prefetch(ModelHandle model, int[] ids, FieldHandle[] fields);

        // --- Dirty Tracking ---

        /// <summary>
        /// Mark a field as dirty (modified) for write-back.
        /// </summary>
        /// <param name="model">Model handle</param>
        /// <param name="id">Record ID</param>
        /// <param name="field">Field handle</param>
        void MarkDirty(ModelHandle model, int id, FieldHandle field);

        /// <summary>
        /// Get all dirty fields for a specific record.
        /// </summary>
        /// <param name="model">Model handle</param>
        /// <param name="id">Record ID</param>
        /// <returns>Collection of field handles that have been modified</returns>
        IEnumerable<FieldHandle> GetDirtyFields(ModelHandle model, int id);

        /// <summary>
        /// Clear dirty state for a specific record.
        /// </summary>
        /// <param name="model">Model handle</param>
        /// <param name="id">Record ID</param>
        void ClearDirty(ModelHandle model, int id);

        // --- Bulk Operations ---

        /// <summary>
        /// Bulk load data for multiple records for a specific field.
        /// This is useful for database integration and testing.
        /// </summary>
        /// <typeparam name="T">The field value type</typeparam>
        /// <param name="model">Model handle</param>
        /// <param name="field">Field handle</param>
        /// <param name="values">Dictionary mapping record IDs to values</param>
        void BulkLoad<T>(ModelHandle model, FieldHandle field, Dictionary<int, T> values);

        // --- Cache Management ---

        /// <summary>
        /// Clear the entire cache.
        /// </summary>
        void Clear();

        /// <summary>
        /// Clear cache for a specific model.
        /// </summary>
        /// <param name="model">Model handle</param>
        void ClearModel(ModelHandle model);
    }
}
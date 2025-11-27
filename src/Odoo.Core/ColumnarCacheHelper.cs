using System;
using System.Collections.Generic;
using System.Linq;

namespace Odoo.Core
{
    /// <summary>
    /// Helper methods for working with columnar cache in examples and tests.
    /// Provides convenient bulk loading that mimics the old row-based API.
    /// </summary>
    public static class ColumnarCacheHelper
    {
        /// <summary>
        /// Bulk load data in row-oriented format and convert to columnar storage.
        /// This helper method makes it easy to seed test data.
        /// </summary>
        /// <param name="cache">The columnar cache to load data into</param>
        /// <param name="modelName">The model name (e.g., "res.partner")</param>
        /// <param name="modelToken">The model token</param>
        /// <param name="records">Dictionary of record ID to field-value dictionaries</param>
        public static void BulkLoadRows(
            this IColumnarCache cache,
            string modelName,
            ModelHandle modelToken,
            Dictionary<int, Dictionary<string, object?>> records)
        {
            if (records.Count == 0)
                return;

            // Collect all field names across all records
            var allFields = new HashSet<string>();
            foreach (var record in records.Values)
            {
                foreach (var field in record.Keys)
                {
                    allFields.Add(field);
                }
            }

            // For each field, build a column and load it
            foreach (var fieldName in allFields)
            {
                // Get field token (we'll use a simple hash for this helper)
                var fieldToken = new FieldHandle(GetFieldToken(modelName, fieldName));

                // Collect all values for this field
                var valuesByType = new Dictionary<Type, Dictionary<int, object>>();

                foreach (var (recordId, fieldValues) in records)
                {
                    if (fieldValues.TryGetValue(fieldName, out var value) && value != null)
                    {
                        var valueType = value.GetType();
                        if (!valuesByType.ContainsKey(valueType))
                        {
                            valuesByType[valueType] = new Dictionary<int, object>();
                        }
                        valuesByType[valueType][recordId] = value;
                    }
                }

                // Load each type separately (columnar storage is type-specific)
                foreach (var (valueType, values) in valuesByType)
                {
                    LoadTypedColumn(cache, modelToken, fieldToken, values);
                }
            }
        }

        private static void LoadTypedColumn(
            IColumnarCache cache,
            ModelHandle modelToken,
            FieldHandle fieldToken,
            Dictionary<int, object> values)
        {
            if (values.Count == 0)
                return;

            // Get the value type from the first value
            var valueType = values.Values.First().GetType();
            
            // Use reflection to call BulkLoad<T> with the correct type
            var method = typeof(IColumnarCache).GetMethod(nameof(IColumnarCache.BulkLoad));
            var genericMethod = method!.MakeGenericMethod(valueType);
            
            // Convert Dictionary<int, object> to Dictionary<int, T>
            var targetDictType = typeof(Dictionary<,>).MakeGenericType(typeof(int), valueType);
            var targetDict = Activator.CreateInstance(targetDictType) as System.Collections.IDictionary;
            
            foreach (var (id, value) in values)
            {
                targetDict![id] = value;
            }
            
            genericMethod.Invoke(cache, new object[] { modelToken, fieldToken, targetDict! });
        }

        private static int GetFieldToken(string modelName, string fieldName)
        {
            // Simple hash function for field tokens
            // In production, this would use the generated ModelSchema tokens
            var combined = $"{modelName}.{fieldName}";
            return combined.GetHashCode() & 0x7FFFFFFF; // Ensure positive
        }

        /// <summary>
        /// Get dirty fields for a record (helper for examples)
        /// </summary>
        public static IEnumerable<string> GetDirtyFieldNames(
            this IColumnarCache cache,
            string modelName,
            ModelHandle modelToken,
            int recordId)
        {
            var dirtyHandles = cache.GetDirtyFields(modelToken, recordId);
            var fieldNames = new List<string>();
            
            foreach (var handle in dirtyHandles)
            {
                // In a real scenario, you'd look up the field name from the handle
                // For this helper, we'll just return the token number as a string
                fieldNames.Add($"field_{handle.Token}");
            }
            
            return fieldNames;
        }
    }
}
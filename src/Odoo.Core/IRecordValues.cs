namespace Odoo.Core
{
    /// <summary>
    /// Base interface for all generated record value classes.
    /// Enables generic handling in pipelines and batch operations.
    ///
    /// Record values classes hold the field values for creating or updating records.
    /// Each field uses RecordValueField&lt;T&gt; to track whether it was explicitly set.
    /// </summary>
    public interface IRecordValues
    {
        /// <summary>
        /// Model name this values object is for (e.g., "res.partner").
        /// Used for routing to the correct pipeline.
        /// </summary>
        string ModelName { get; }

        /// <summary>
        /// Convert to dictionary for Python interop or legacy code.
        /// Only includes fields where IsSet == true.
        /// </summary>
        /// <returns>Dictionary with field names as keys and values</returns>
        Dictionary<string, object?> ToDictionary();

        /// <summary>
        /// Get list of field names that are set.
        /// </summary>
        /// <returns>Enumerable of field names that have values</returns>
        IEnumerable<string> GetSetFields();
    }

    /// <summary>
    /// Typed interface for record values with record type information.
    /// Enables type-safe batch operations and better compile-time checking.
    /// </summary>
    /// <typeparam name="TRecord">The record type this values class creates</typeparam>
    public interface IRecordValues<TRecord> : IRecordValues
        where TRecord : IOdooRecord { }

    /// <summary>
    /// Extension methods for IRecordValues providing type-safe field access.
    /// </summary>
    public static class RecordValuesExtensions
    {
        /// <summary>
        /// Get a field value with type safety.
        /// Returns null if the field is not set.
        /// </summary>
        /// <typeparam name="T">Expected value type</typeparam>
        /// <param name="vals">The values object</param>
        /// <param name="fieldName">Odoo field name (snake_case)</param>
        /// <returns>The value or null if not set</returns>
        public static T? Get<T>(this IRecordValues vals, string fieldName)
        {
            var dict = vals.ToDictionary();
            if (dict.TryGetValue(fieldName, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            return default;
        }

        /// <summary>
        /// Check if a field is set and get its value in one call.
        /// </summary>
        /// <typeparam name="T">Expected value type</typeparam>
        /// <param name="vals">The values object</param>
        /// <param name="fieldName">Odoo field name (snake_case)</param>
        /// <param name="value">The value if set</param>
        /// <returns>True if field was set and has correct type</returns>
        public static bool TryGet<T>(this IRecordValues vals, string fieldName, out T value)
        {
            var dict = vals.ToDictionary();
            if (dict.TryGetValue(fieldName, out var obj) && obj is T typedValue)
            {
                value = typedValue;
                return true;
            }
            value = default!;
            return false;
        }

        /// <summary>
        /// Check if a specific field is set.
        /// </summary>
        /// <param name="vals">The values object</param>
        /// <param name="fieldName">Odoo field name (snake_case)</param>
        /// <returns>True if field is set</returns>
        public static bool IsSet(this IRecordValues vals, string fieldName)
        {
            return vals.GetSetFields().Contains(fieldName);
        }
    }
}

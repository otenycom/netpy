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
}

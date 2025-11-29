namespace Odoo.Core
{
    /// <summary>
    /// Wrapper for field values that tracks whether a value was explicitly set.
    /// Used in record values classes for record creation/updates.
    ///
    /// Key features:
    /// - Mutable (reference type) for addon modification during pipeline execution
    /// - Implicit conversion from T for clean object initializer syntax
    /// - No reflection needed to check IsSet
    ///
    /// Example usage:
    /// <code>
    /// var values = new ResPartnerValues { Name = "Alice" }; // Name.IsSet == true
    /// if (values.Name.IsSet) { ... }
    /// </code>
    /// </summary>
    /// <typeparam name="T">The type of the field value</typeparam>
    public sealed class RecordValueField<T>
    {
        /// <summary>
        /// The field value. May be default if IsSet is false.
        /// </summary>
        public T? Value { get; set; }

        /// <summary>
        /// Indicates whether this field was explicitly set.
        /// Enables distinguishing between "not set" and "set to null/default".
        /// </summary>
        public bool IsSet { get; private set; }

        /// <summary>
        /// Creates an unset field.
        /// </summary>
        public RecordValueField() { }

        /// <summary>
        /// Creates a field with a value (marks as set).
        /// </summary>
        /// <param name="value">The value to set</param>
        public RecordValueField(T value)
        {
            Value = value;
            IsSet = true;
        }

        /// <summary>
        /// Implicit conversion from T to RecordValueField&lt;T&gt;.
        /// Enables clean syntax: field = value (instead of field = new RecordValueField&lt;T&gt;(value))
        /// </summary>
        /// <param name="value">The value to wrap</param>
        public static implicit operator RecordValueField<T>(T value) => new(value);

        /// <summary>
        /// Reset to unset state.
        /// </summary>
        public void Clear()
        {
            Value = default;
            IsSet = false;
        }

        /// <summary>
        /// Explicitly set value (updates IsSet flag).
        /// Use when you need to set after construction.
        /// </summary>
        /// <param name="value">The value to set</param>
        public void Set(T value)
        {
            Value = value;
            IsSet = true;
        }

        /// <summary>
        /// Get the value, throwing if not set.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if the field is not set</exception>
        public T GetRequired()
        {
            if (!IsSet)
                throw new InvalidOperationException("Field value is not set");
            return Value!;
        }

        /// <summary>
        /// Get the value or a default if not set.
        /// </summary>
        /// <param name="defaultValue">Default value to return if not set</param>
        public T GetOrDefault(T defaultValue) => IsSet ? Value! : defaultValue;

        public override string ToString() => IsSet ? $"Set({Value})" : "Unset";
    }
}

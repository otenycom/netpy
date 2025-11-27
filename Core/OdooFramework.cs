using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odoo.Core
{
    // --- Attributes for Source Generator ---

    /// <summary>
    /// Marks an interface as an Odoo model definition.
    /// The Source Generator will create a corresponding record struct.
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface)]
    public class OdooModelAttribute : Attribute
    {
        public string ModelName { get; }
        
        public OdooModelAttribute(string modelName) => ModelName = modelName;
    }

    /// <summary>
    /// Maps a property to an Odoo field with its technical name.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class OdooFieldAttribute : Attribute
    {
        public string TechnicalName { get; }
        
        public OdooFieldAttribute(string technicalName) => TechnicalName = technicalName;
    }

    // --- The Cache / Data Layer ---

    /// <summary>
    /// The centralized data store. This interacts with the database/backend
    /// and maintains transactional dirty state.
    /// </summary>
    public interface IValueCache
    {
        /// <summary>
        /// Retrieve a value from the cache for a specific model/record/field.
        /// </summary>
        T GetValue<T>(string model, int id, string field);
        
        /// <summary>
        /// Store a value in the cache for a specific model/record/field.
        /// </summary>
        void SetValue<T>(string model, int id, string field, T value);
        
        /// <summary>
        /// Check if a value exists in the cache.
        /// </summary>
        bool HasValue(string model, int id, string field);
        
        /// <summary>
        /// Mark a field as dirty (modified).
        /// </summary>
        void MarkDirty(string model, int id, string field);
        
        /// <summary>
        /// Get all dirty fields for a record.
        /// </summary>
        IEnumerable<string> GetDirtyFields(string model, int id);
        
        /// <summary>
        /// Clear dirty state for a record.
        /// </summary>
        void ClearDirty(string model, int id);
    }

    // --- The Environment ---

    /// <summary>
    /// Represents the execution context for ORM operations.
    /// Contains user info, cache, and factory methods.
    /// </summary>
    public interface IEnvironment
    {
        /// <summary>
        /// The current user ID.
        /// </summary>
        int UserId { get; }
        
        /// <summary>
        /// The value cache for data storage.
        /// </summary>
        IValueCache Cache { get; }
        
        /// <summary>
        /// Factory method to get a recordset wrapper for a specific interface.
        /// </summary>
        RecordSet<T> GetModel<T>() where T : IOdooRecord;
        
        /// <summary>
        /// Create a recordset with specific IDs.
        /// </summary>
        RecordSet<T> CreateRecordSet<T>(int[] ids) where T : IOdooRecord;
    }

    // --- The Generic RecordSet ---

    /// <summary>
    /// Represents a collection of records (IDs) for a specific model.
    /// The 'T' is the Model Interface (e.g., IPartner).
    /// This is a lightweight struct that only holds IDs and environment reference.
    /// </summary>
    public readonly struct RecordSet<T> : IEnumerable<T> where T : IOdooRecord
    {
        public readonly IEnvironment Env;
        public readonly string ModelName;
        public readonly int[] Ids;

        // The Factory creates the concrete struct wrapper for a single ID
        private readonly Func<IEnvironment, int, T> _recordFactory;

        public RecordSet(
            IEnvironment env, 
            string modelName, 
            int[] ids, 
            Func<IEnvironment, int, T> factory)
        {
            Env = env;
            ModelName = modelName;
            Ids = ids;
            _recordFactory = factory;
        }

        /// <summary>
        /// Access a record by index.
        /// </summary>
        public T this[int index] => _recordFactory(Env, Ids[index]);

        /// <summary>
        /// Number of records in the set.
        /// </summary>
        public int Count => Ids.Length;

        /// <summary>
        /// Check if the recordset is empty.
        /// </summary>
        public bool IsEmpty => Ids.Length == 0;

        /// <summary>
        /// Get the first record (or default if empty).
        /// </summary>
        public T? FirstOrDefault => IsEmpty ? default : this[0];

        /// <summary>
        /// Iterate over all records.
        /// </summary>
        public IEnumerator<T> GetEnumerator()
        {
            foreach (var id in Ids) 
                yield return _recordFactory(Env, id);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Filter the recordset based on a predicate.
        /// </summary>
        public RecordSet<T> Where(Func<T, bool> predicate)
        {
            var filteredIds = new List<int>();
            foreach (var id in Ids)
            {
                var record = _recordFactory(Env, id);
                if (predicate(record))
                    filteredIds.Add(id);
            }
            return new RecordSet<T>(Env, ModelName, filteredIds.ToArray(), _recordFactory);
        }

        /// <summary>
        /// Map the recordset to another type.
        /// </summary>
        public IEnumerable<TResult> Select<TResult>(Func<T, TResult> selector)
        {
            foreach (var id in Ids)
                yield return selector(_recordFactory(Env, id));
        }
    }

    /// <summary>
    /// Base interface that all Odoo Model Interfaces must inherit from.
    /// Provides access to record ID and environment.
    /// </summary>
    public interface IOdooRecord 
    {
        /// <summary>
        /// The database ID of this record.
        /// </summary>
        int Id { get; }
        
        /// <summary>
        /// The environment context.
        /// </summary>
        IEnvironment Env { get; }
    }

    // --- Search Domain Support ---

    /// <summary>
    /// Represents a search domain for querying records.
    /// This is a simplified version - can be extended with proper Odoo domain syntax.
    /// </summary>
    public class SearchDomain
    {
        private readonly List<(string Field, string Operator, object Value)> _conditions = new();

        public SearchDomain Where(string field, string op, object value)
        {
            _conditions.Add((field, op, value));
            return this;
        }

        public IReadOnlyList<(string Field, string Operator, object Value)> Conditions => _conditions;
    }

    // --- Extension Methods ---

    /// <summary>
    /// Extension methods for IEnvironment to provide fluent API.
    /// </summary>
    public static class EnvironmentExtensions
    {
        /// <summary>
        /// Search for records matching a domain.
        /// This is a placeholder - actual implementation would query the database.
        /// </summary>
        public static Task<RecordSet<T>> SearchAsync<T>(
            this IEnvironment env, 
            SearchDomain domain) where T : IOdooRecord
        {
            // Placeholder implementation
            // In reality, this would execute a database query
            return Task.FromResult(env.GetModel<T>());
        }

        /// <summary>
        /// Create a new record.
        /// </summary>
        public static Task<T> CreateAsync<T>(
            this IEnvironment env, 
            Dictionary<string, object> values) where T : IOdooRecord
        {
            // Placeholder implementation
            throw new NotImplementedException("Create operation not yet implemented");
        }
    }
}
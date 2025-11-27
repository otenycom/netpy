using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Odoo.Core
{
    /// <summary>
    /// Simple in-memory implementation of IValueCache.
    /// In production, this would be backed by a database and transaction manager.
    /// </summary>
    public class SimpleValueCache : IValueCache
    {
        // Cache structure: Model -> RecordId -> Field -> Value
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, ConcurrentDictionary<string, object?>>> _cache = new();
        
        // Dirty tracking: Model -> RecordId -> Set of Field Names
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, HashSet<string>>> _dirtyFields = new();

        public T GetValue<T>(string model, int id, string field)
        {
            if (_cache.TryGetValue(model, out var modelCache) &&
                modelCache.TryGetValue(id, out var recordCache) &&
                recordCache.TryGetValue(field, out var value))
            {
                if (value is T typedValue)
                    return typedValue;
                
                // Handle type conversion for common cases
                if (value == null)
                    return default(T)!;
                
                try
                {
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return default(T)!;
                }
            }

            return default(T)!;
        }

        public void SetValue<T>(string model, int id, string field, T value)
        {
            var modelCache = _cache.GetOrAdd(model, _ => new ConcurrentDictionary<int, ConcurrentDictionary<string, object?>>());
            var recordCache = modelCache.GetOrAdd(id, _ => new ConcurrentDictionary<string, object?>());
            recordCache[field] = value;
        }

        public bool HasValue(string model, int id, string field)
        {
            return _cache.TryGetValue(model, out var modelCache) &&
                   modelCache.TryGetValue(id, out var recordCache) &&
                   recordCache.ContainsKey(field);
        }

        public void MarkDirty(string model, int id, string field)
        {
            var modelDirty = _dirtyFields.GetOrAdd(model, _ => new ConcurrentDictionary<int, HashSet<string>>());
            var recordDirty = modelDirty.GetOrAdd(id, _ => new HashSet<string>());
            
            lock (recordDirty)
            {
                recordDirty.Add(field);
            }
        }

        public IEnumerable<string> GetDirtyFields(string model, int id)
        {
            if (_dirtyFields.TryGetValue(model, out var modelDirty) &&
                modelDirty.TryGetValue(id, out var recordDirty))
            {
                lock (recordDirty)
                {
                    return recordDirty.ToList();
                }
            }

            return Enumerable.Empty<string>();
        }

        public void ClearDirty(string model, int id)
        {
            if (_dirtyFields.TryGetValue(model, out var modelDirty))
            {
                modelDirty.TryRemove(id, out _);
            }
        }

        /// <summary>
        /// Bulk load data for multiple records (useful for batch operations).
        /// </summary>
        public void BulkLoad(string model, Dictionary<int, Dictionary<string, object?>> records)
        {
            var modelCache = _cache.GetOrAdd(model, _ => new ConcurrentDictionary<int, ConcurrentDictionary<string, object?>>());
            
            foreach (var (recordId, fields) in records)
            {
                var recordCache = modelCache.GetOrAdd(recordId, _ => new ConcurrentDictionary<string, object?>());
                foreach (var (field, value) in fields)
                {
                    recordCache[field] = value;
                }
            }
        }

        /// <summary>
        /// Clear the entire cache.
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
            _dirtyFields.Clear();
        }

        /// <summary>
        /// Clear cache for a specific model.
        /// </summary>
        public void ClearModel(string model)
        {
            _cache.TryRemove(model, out _);
            _dirtyFields.TryRemove(model, out _);
        }
    }
}
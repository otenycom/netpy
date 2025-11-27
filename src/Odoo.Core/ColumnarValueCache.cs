using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Odoo.Core
{
    /// <summary>
    /// High-performance columnar cache implementation using Data-Oriented Design.
    /// Stores data in Structure of Arrays (SoA) format for optimal memory locality.
    /// <para>
    /// <b>THREAD SAFETY:</b> This class is explicitly <b>NOT thread-safe</b>.
    /// It is designed to be used within a single transaction/environment context.
    /// Sharing this instance across threads without external synchronization will lead to race conditions.
    /// </para>
    /// </summary>
    public class ColumnarValueCache : IColumnarCache
    {
        // Column storage: (Model, Field) -> ColumnStorage
        private readonly Dictionary<(int ModelToken, int FieldToken), IColumnStorage> _columns = new();
        
        // Dirty tracking: (Model, Record ID) -> Set of Field Tokens
        private readonly Dictionary<(int ModelToken, int RecordId), HashSet<int>> _dirtyFields = new();

        // --- Batch Operations ---

        public ReadOnlySpan<T> GetColumnSpan<T>(ModelHandle model, int[] ids, FieldHandle field)
        {
            var key = (model.Token, field.Token);
            
            if (!_columns.TryGetValue(key, out var storage))
            {
                // Column doesn't exist yet - return default values
                // In production, this would trigger a database fetch
                return new T[ids.Length];
            }

            return storage.GetSpan<T>(ids);
        }

        public void SetColumnValues<T>(ModelHandle model, int[] ids, FieldHandle field, ReadOnlySpan<T> values)
        {
            if (ids.Length != values.Length)
                throw new ArgumentException("IDs and values must have same length");

            var key = (model.Token, field.Token);
            
            if (!_columns.TryGetValue(key, out var storage))
            {
                storage = new ColumnStorage<T>();
                _columns[key] = storage;
            }
            
            storage.SetValues(ids, values);

            // Mark all as dirty
            for (int i = 0; i < ids.Length; i++)
            {
                MarkDirtyInternal(model.Token, ids[i], field.Token);
            }
        }

        // --- Single Record Operations ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetValue<T>(ModelHandle model, int id, FieldHandle field)
        {
            var key = (model.Token, field.Token);
            
            if (!_columns.TryGetValue(key, out var storage))
                return default(T)!;

            return storage.GetSingleValue<T>(id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetValue<T>(ModelHandle model, int id, FieldHandle field, T value)
        {
            var key = (model.Token, field.Token);
            
            if (!_columns.TryGetValue(key, out var storage))
            {
                storage = new ColumnStorage<T>();
                _columns[key] = storage;
            }
            
            storage.SetSingleValue(id, value);
        }

        public bool HasValue(ModelHandle model, int id, FieldHandle field)
        {
            var key = (model.Token, field.Token);
            return _columns.TryGetValue(key, out var storage) && storage.HasValue(id);
        }

        // --- Prefetch Operations ---

        public void Prefetch(ModelHandle model, int[] ids, FieldHandle[] fields)
        {
            // In production, this would batch-fetch from database
            // For now, ensure columns exist
            foreach (var field in fields)
            {
                var key = (model.Token, field.Token);
                if (!_columns.ContainsKey(key))
                {
                    // Would trigger load from database
                }
            }
        }

        // --- Dirty Tracking ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkDirty(ModelHandle model, int id, FieldHandle field)
        {
            MarkDirtyInternal(model.Token, id, field.Token);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MarkDirtyInternal(int modelToken, int recordId, int fieldToken)
        {
            var key = (modelToken, recordId);
            
            if (!_dirtyFields.TryGetValue(key, out var fields))
            {
                fields = new HashSet<int>();
                _dirtyFields[key] = fields;
            }
            
            fields.Add(fieldToken);
        }

        public IEnumerable<FieldHandle> GetDirtyFields(ModelHandle model, int id)
        {
            var key = (model.Token, id);
            
            if (_dirtyFields.TryGetValue(key, out var fields))
            {
                return fields.Select(token => new FieldHandle(token)).ToList();
            }

            return Enumerable.Empty<FieldHandle>();
        }

        public void ClearDirty(ModelHandle model, int id)
        {
            var key = (model.Token, id);
            _dirtyFields.Remove(key);
        }

        // --- Cache Management ---

        public void Clear()
        {
            _columns.Clear();
            _dirtyFields.Clear();
        }

        public void ClearModel(ModelHandle model)
        {
            // Remove all columns for this model
            var keysToRemove = _columns.Keys.Where(k => k.ModelToken == model.Token).ToList();
            foreach (var key in keysToRemove)
            {
                _columns.Remove(key);
            }

            // Clear dirty tracking for this model
            var dirtyKeysToRemove = _dirtyFields.Keys.Where(k => k.ModelToken == model.Token).ToList();
            foreach (var key in dirtyKeysToRemove)
            {
                _dirtyFields.Remove(key);
            }
        }

        // --- Bulk Load (for database integration) ---

        /// <summary>
        /// Bulk load data for multiple records and fields.
        /// This is the primary database integration point.
        /// </summary>
        public void BulkLoad<T>(ModelHandle model, FieldHandle field, Dictionary<int, T> values)
        {
            if (values.Count == 0)
                return;

            var key = (model.Token, field.Token);
            
            if (!_columns.TryGetValue(key, out var storage))
            {
                storage = new ColumnStorage<T>();
                _columns[key] = storage;
            }

            foreach (var (id, value) in values)
            {
                storage.SetSingleValue(id, value);
            }
        }
    }

    // --- Column Storage Implementation ---

    /// <summary>
    /// Interface for type-erased column storage.
    /// </summary>
    internal interface IColumnStorage
    {
        ReadOnlySpan<T> GetSpan<T>(int[] ids);
        void SetValues<T>(int[] ids, ReadOnlySpan<T> values);
        T GetSingleValue<T>(int id);
        void SetSingleValue<T>(int id, T value);
        bool HasValue(int id);
    }

    /// <summary>
    /// Type-specific column storage using contiguous arrays.
    /// Optimized for cache locality and batch operations.
    /// </summary>
    internal class ColumnStorage<T> : IColumnStorage
    {
        private T[] _data;
        private Dictionary<int, int> _idToIndex;
        private int _count;
        private const int InitialCapacity = 16;

        public ColumnStorage()
        {
            _data = ArrayPool<T>.Shared.Rent(InitialCapacity);
            _idToIndex = new Dictionary<int, int>();
            _count = 0;
        }

        public ReadOnlySpan<TValue> GetSpan<TValue>(int[] ids)
        {
            if (typeof(TValue) != typeof(T))
                throw new InvalidOperationException($"Type mismatch: expected {typeof(T)}, got {typeof(TValue)}");

            // Build result array with values for requested IDs
            var result = new TValue[ids.Length];
            
            for (int i = 0; i < ids.Length; i++)
            {
                if (_idToIndex.TryGetValue(ids[i], out int index))
                {
                    result[i] = (TValue)(object)_data[index]!;
                }
                else
                {
                    result[i] = default!;
                }
            }

            return result;
        }

        public void SetValues<TValue>(int[] ids, ReadOnlySpan<TValue> values)
        {
            if (typeof(TValue) != typeof(T))
                throw new InvalidOperationException($"Type mismatch: expected {typeof(T)}, got {typeof(TValue)}");

            for (int i = 0; i < ids.Length; i++)
            {
                SetSingleValue(ids[i], (T)(object)values[i]!);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TValue GetSingleValue<TValue>(int id)
        {
            if (typeof(TValue) != typeof(T))
                throw new InvalidOperationException($"Type mismatch: expected {typeof(T)}, got {typeof(TValue)}");

            if (_idToIndex.TryGetValue(id, out int index))
            {
                return (TValue)(object)_data[index]!;
            }

            return default!;
        }

        public void SetSingleValue<TValue>(int id, TValue value)
        {
            if (typeof(TValue) != typeof(T))
                throw new InvalidOperationException($"Type mismatch: expected {typeof(T)}, got {typeof(TValue)}");

            if (_idToIndex.TryGetValue(id, out int index))
            {
                // Update existing value
                _data[index] = (T)(object)value!;
            }
            else
            {
                // Add new value
                EnsureCapacity(_count + 1);
                _idToIndex[id] = _count;
                _data[_count] = (T)(object)value!;
                _count++;
            }
        }

        public bool HasValue(int id)
        {
            return _idToIndex.ContainsKey(id);
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _data.Length)
                return;

            int newCapacity = _data.Length * 2;
            while (newCapacity < required)
                newCapacity *= 2;

            var newData = ArrayPool<T>.Shared.Rent(newCapacity);
            Array.Copy(_data, newData, _count);
            
            ArrayPool<T>.Shared.Return(_data, clearArray: true);
            _data = newData;
        }

        ~ColumnStorage()
        {
            if (_data != null)
            {
                ArrayPool<T>.Shared.Return(_data, clearArray: true);
            }
        }
    }
}
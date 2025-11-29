using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Odoo.Core
{
    /// <summary>
    /// Centralized dirty tracking for field modifications.
    /// Tracks which fields on which records have been modified and need to be flushed to database.
    /// <para>
    /// <b>THREAD SAFETY:</b> This class is NOT thread-safe.
    /// It is designed to be owned by a single <see cref="IEnvironment"/> context.
    /// </para>
    /// </summary>
    public class DirtyTracker
    {
        // Model Token -> (Record ID -> Set of Field Tokens)
        private readonly Dictionary<int, Dictionary<int, HashSet<int>>> _dirtyByModel = new();
        
        // Ordered list of (Model, RecordId, Field) for maintaining write order
        private readonly List<(int ModelToken, int RecordId, int FieldToken)> _writeOrder = new();

        /// <summary>
        /// Mark a field as dirty for a specific record.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkDirty(ModelHandle model, int recordId, FieldHandle field)
        {
            MarkDirtyInternal(model.Token, recordId, field.Token);
        }

        /// <summary>
        /// Mark a field as dirty for a specific record (internal).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MarkDirtyInternal(int modelToken, int recordId, int fieldToken)
        {
            if (!_dirtyByModel.TryGetValue(modelToken, out var recordFields))
            {
                recordFields = new Dictionary<int, HashSet<int>>();
                _dirtyByModel[modelToken] = recordFields;
            }

            if (!recordFields.TryGetValue(recordId, out var fields))
            {
                fields = new HashSet<int>();
                recordFields[recordId] = fields;
            }

            // Only add to write order if this is a new dirty field
            if (fields.Add(fieldToken))
            {
                _writeOrder.Add((modelToken, recordId, fieldToken));
            }
        }

        /// <summary>
        /// Mark multiple fields as dirty for a specific record.
        /// </summary>
        public void MarkDirty(ModelHandle model, int recordId, IEnumerable<FieldHandle> fields)
        {
            foreach (var field in fields)
            {
                MarkDirtyInternal(model.Token, recordId, field.Token);
            }
        }

        /// <summary>
        /// Get all dirty fields for a specific record.
        /// </summary>
        public IEnumerable<FieldHandle> GetDirtyFields(ModelHandle model, int recordId)
        {
            if (_dirtyByModel.TryGetValue(model.Token, out var recordFields) &&
                recordFields.TryGetValue(recordId, out var fields))
            {
                return fields.Select(token => new FieldHandle(token));
            }

            return Enumerable.Empty<FieldHandle>();
        }

        /// <summary>
        /// Check if a record has any dirty fields.
        /// </summary>
        public bool IsDirty(ModelHandle model, int recordId)
        {
            return _dirtyByModel.TryGetValue(model.Token, out var recordFields) &&
                   recordFields.ContainsKey(recordId);
        }

        /// <summary>
        /// Check if a specific field on a record is dirty.
        /// </summary>
        public bool IsFieldDirty(ModelHandle model, int recordId, FieldHandle field)
        {
            return _dirtyByModel.TryGetValue(model.Token, out var recordFields) &&
                   recordFields.TryGetValue(recordId, out var fields) &&
                   fields.Contains(field.Token);
        }

        /// <summary>
        /// Get all dirty records for a model.
        /// </summary>
        public IEnumerable<(int RecordId, IEnumerable<FieldHandle> DirtyFields)> GetDirtyRecords(ModelHandle model)
        {
            if (_dirtyByModel.TryGetValue(model.Token, out var recordFields))
            {
                foreach (var (recordId, fields) in recordFields)
                {
                    yield return (recordId, fields.Select(t => new FieldHandle(t)));
                }
            }
        }

        /// <summary>
        /// Get all dirty data grouped by model.
        /// Returns: Model Token -> (Record ID -> Set of Field Tokens)
        /// </summary>
        public IEnumerable<(int ModelToken, IEnumerable<(int RecordId, IEnumerable<int> FieldTokens)>)> GetAllDirty()
        {
            foreach (var (modelToken, recordFields) in _dirtyByModel)
            {
                var records = recordFields.Select(kv => (kv.Key, (IEnumerable<int>)kv.Value));
                yield return (modelToken, records);
            }
        }

        /// <summary>
        /// Get dirty records in write order (maintains the order fields were modified).
        /// </summary>
        public IEnumerable<(int ModelToken, int RecordId, int FieldToken)> GetWriteOrder()
        {
            return _writeOrder;
        }

        /// <summary>
        /// Clear dirty state for a specific record.
        /// </summary>
        public void ClearRecord(ModelHandle model, int recordId)
        {
            if (_dirtyByModel.TryGetValue(model.Token, out var recordFields))
            {
                recordFields.Remove(recordId);
                
                // Also remove from write order
                _writeOrder.RemoveAll(x => x.ModelToken == model.Token && x.RecordId == recordId);
            }
        }

        /// <summary>
        /// Clear dirty state for a specific model.
        /// </summary>
        public void ClearModel(ModelHandle model)
        {
            _dirtyByModel.Remove(model.Token);
            _writeOrder.RemoveAll(x => x.ModelToken == model.Token);
        }

        /// <summary>
        /// Clear all dirty state.
        /// </summary>
        public void ClearAll()
        {
            _dirtyByModel.Clear();
            _writeOrder.Clear();
        }

        /// <summary>
        /// Check if there are any dirty records.
        /// </summary>
        public bool HasDirty => _dirtyByModel.Count > 0;

        /// <summary>
        /// Get count of dirty records.
        /// </summary>
        public int DirtyRecordCount => _dirtyByModel.Values.Sum(r => r.Count);

        /// <summary>
        /// Get all model tokens that have dirty records.
        /// </summary>
        public IEnumerable<int> GetDirtyModels()
        {
            return _dirtyByModel.Keys;
        }

        /// <summary>
        /// Get just the record IDs that are dirty for a model.
        /// </summary>
        public IEnumerable<int> GetDirtyRecordIds(ModelHandle model)
        {
            if (_dirtyByModel.TryGetValue(model.Token, out var recordFields))
            {
                return recordFields.Keys;
            }
            return Enumerable.Empty<int>();
        }
    }
}
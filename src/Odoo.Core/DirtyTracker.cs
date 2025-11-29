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
        // Model -> (Record ID -> Set of Fields)
        private readonly Dictionary<
            ModelHandle,
            Dictionary<RecordId, HashSet<FieldHandle>>
        > _dirtyByModel = new();

        // Ordered list of (Model, RecordId, Field) for maintaining write order
        private readonly List<(
            ModelHandle Model,
            RecordId RecordId,
            FieldHandle Field
        )> _writeOrder = new();

        /// <summary>
        /// Mark a field as dirty for a specific record.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkDirty(ModelHandle model, RecordId recordId, FieldHandle field)
        {
            if (!_dirtyByModel.TryGetValue(model, out var recordFields))
            {
                recordFields = new Dictionary<RecordId, HashSet<FieldHandle>>();
                _dirtyByModel[model] = recordFields;
            }

            if (!recordFields.TryGetValue(recordId, out var fields))
            {
                fields = new HashSet<FieldHandle>();
                recordFields[recordId] = fields;
            }

            // Only add to write order if this is a new dirty field
            if (fields.Add(field))
            {
                _writeOrder.Add((model, recordId, field));
            }
        }

        /// <summary>
        /// Mark multiple fields as dirty for a specific record.
        /// </summary>
        public void MarkDirty(ModelHandle model, RecordId recordId, IEnumerable<FieldHandle> fields)
        {
            foreach (var field in fields)
            {
                MarkDirty(model, recordId, field);
            }
        }

        /// <summary>
        /// Get all dirty fields for a specific record.
        /// </summary>
        public IEnumerable<FieldHandle> GetDirtyFields(ModelHandle model, RecordId recordId)
        {
            if (
                _dirtyByModel.TryGetValue(model, out var recordFields)
                && recordFields.TryGetValue(recordId, out var fields)
            )
            {
                return fields;
            }

            return Enumerable.Empty<FieldHandle>();
        }

        /// <summary>
        /// Check if a record has any dirty fields.
        /// </summary>
        public bool IsDirty(ModelHandle model, RecordId recordId)
        {
            return _dirtyByModel.TryGetValue(model, out var recordFields)
                && recordFields.ContainsKey(recordId);
        }

        /// <summary>
        /// Check if a specific field on a record is dirty.
        /// </summary>
        public bool IsFieldDirty(ModelHandle model, RecordId recordId, FieldHandle field)
        {
            return _dirtyByModel.TryGetValue(model, out var recordFields)
                && recordFields.TryGetValue(recordId, out var fields)
                && fields.Contains(field);
        }

        /// <summary>
        /// Get all dirty records for a model.
        /// </summary>
        public IEnumerable<(
            RecordId RecordId,
            IEnumerable<FieldHandle> DirtyFields
        )> GetDirtyRecords(ModelHandle model)
        {
            if (_dirtyByModel.TryGetValue(model, out var recordFields))
            {
                foreach (var (recordId, fields) in recordFields)
                {
                    yield return (recordId, fields);
                }
            }
        }

        /// <summary>
        /// Get all dirty data grouped by model.
        /// Returns: Model -> (Record ID -> Set of Fields)
        /// </summary>
        public IEnumerable<(
            ModelHandle Model,
            IEnumerable<(RecordId RecordId, IEnumerable<FieldHandle> Fields)>
        )> GetAllDirty()
        {
            foreach (var (model, recordFields) in _dirtyByModel)
            {
                var records = recordFields.Select(kv =>
                    (kv.Key, (IEnumerable<FieldHandle>)kv.Value)
                );
                yield return (model, records);
            }
        }

        /// <summary>
        /// Get dirty records in write order (maintains the order fields were modified).
        /// </summary>
        public IEnumerable<(
            ModelHandle Model,
            RecordId RecordId,
            FieldHandle Field
        )> GetWriteOrder()
        {
            return _writeOrder;
        }

        /// <summary>
        /// Clear dirty state for a specific record.
        /// </summary>
        public void ClearRecord(ModelHandle model, RecordId recordId)
        {
            if (_dirtyByModel.TryGetValue(model, out var recordFields))
            {
                recordFields.Remove(recordId);

                // Also remove from write order
                _writeOrder.RemoveAll(x => x.Model == model && x.RecordId == recordId);
            }
        }

        /// <summary>
        /// Clear dirty state for a specific model.
        /// </summary>
        public void ClearModel(ModelHandle model)
        {
            _dirtyByModel.Remove(model);
            _writeOrder.RemoveAll(x => x.Model == model);
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
        /// Get all models that have dirty records.
        /// </summary>
        public IEnumerable<ModelHandle> GetDirtyModels()
        {
            return _dirtyByModel.Keys;
        }

        /// <summary>
        /// Get just the record IDs that are dirty for a model.
        /// </summary>
        public IEnumerable<RecordId> GetDirtyRecordIds(ModelHandle model)
        {
            if (_dirtyByModel.TryGetValue(model, out var recordFields))
            {
                return recordFields.Keys;
            }
            return Enumerable.Empty<RecordId>();
        }
    }
}

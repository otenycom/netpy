using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Odoo.Core
{
    /// <summary>
    /// Tracks fields that need recomputation due to dependency changes.
    /// Implements Odoo's "modified" pattern where changes to fields trigger
    /// recomputation of dependent computed fields.
    /// <para>
    /// <b>THREAD SAFETY:</b> This class is NOT thread-safe.
    /// It is designed to be owned by a single <see cref="IEnvironment"/> context.
    /// </para>
    /// </summary>
    public class ComputeTracker
    {
        // Model Token -> (Record ID -> Set of Field Tokens needing recompute)
        private readonly Dictionary<int, Dictionary<int, HashSet<int>>> _toRecompute = new();
        
        // Field dependency graph: (Model, Field) -> List of (dependent Model, dependent Field)
        private readonly Dictionary<(int ModelToken, int FieldToken), List<(int ModelToken, int FieldToken)>> _dependencyGraph = new();

        /// <summary>
        /// Register a dependency between fields.
        /// When sourceField changes, dependentField should be recomputed.
        /// </summary>
        /// <param name="sourceModel">Model containing the source field</param>
        /// <param name="sourceField">The field that triggers recomputation</param>
        /// <param name="dependentModel">Model containing the dependent field</param>
        /// <param name="dependentField">The field that needs recomputation</param>
        public void RegisterDependency(
            ModelHandle sourceModel, FieldHandle sourceField,
            ModelHandle dependentModel, FieldHandle dependentField)
        {
            var key = (sourceModel.Token, sourceField.Token);
            
            if (!_dependencyGraph.TryGetValue(key, out var dependents))
            {
                dependents = new List<(int, int)>();
                _dependencyGraph[key] = dependents;
            }
            
            var dep = (dependentModel.Token, dependentField.Token);
            if (!dependents.Contains(dep))
            {
                dependents.Add(dep);
            }
        }

        /// <summary>
        /// Called when a field has been modified. 
        /// Marks all dependent computed fields for recomputation.
        /// </summary>
        /// <param name="model">Model that was modified</param>
        /// <param name="recordId">Record that was modified</param>
        /// <param name="field">Field that was modified</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Modified(ModelHandle model, int recordId, FieldHandle field)
        {
            ModifiedInternal(model.Token, recordId, field.Token);
        }

        /// <summary>
        /// Called when multiple fields have been modified.
        /// </summary>
        public void Modified(ModelHandle model, int recordId, IEnumerable<FieldHandle> fields)
        {
            foreach (var field in fields)
            {
                ModifiedInternal(model.Token, recordId, field.Token);
            }
        }

        /// <summary>
        /// Called when a field has been modified on multiple records.
        /// </summary>
        public void Modified(ModelHandle model, int[] recordIds, FieldHandle field)
        {
            foreach (var recordId in recordIds)
            {
                ModifiedInternal(model.Token, recordId, field.Token);
            }
        }

        private void ModifiedInternal(int modelToken, int recordId, int fieldToken)
        {
            var key = (modelToken, fieldToken);
            
            if (!_dependencyGraph.TryGetValue(key, out var dependents))
            {
                return; // No computed fields depend on this field
            }

            // Mark all dependent fields for recomputation
            foreach (var (depModelToken, depFieldToken) in dependents)
            {
                MarkToRecompute(depModelToken, recordId, depFieldToken);
            }
        }

        /// <summary>
        /// Mark a field for recomputation on a specific record.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkToRecompute(ModelHandle model, int recordId, FieldHandle field)
        {
            MarkToRecompute(model.Token, recordId, field.Token);
        }

        private void MarkToRecompute(int modelToken, int recordId, int fieldToken)
        {
            if (!_toRecompute.TryGetValue(modelToken, out var recordFields))
            {
                recordFields = new Dictionary<int, HashSet<int>>();
                _toRecompute[modelToken] = recordFields;
            }

            if (!recordFields.TryGetValue(recordId, out var fields))
            {
                fields = new HashSet<int>();
                recordFields[recordId] = fields;
            }

            fields.Add(fieldToken);
        }

        /// <summary>
        /// Check if a field needs recomputation for a specific record.
        /// </summary>
        public bool NeedsRecompute(ModelHandle model, int recordId, FieldHandle field)
        {
            return _toRecompute.TryGetValue(model.Token, out var recordFields) &&
                   recordFields.TryGetValue(recordId, out var fields) &&
                   fields.Contains(field.Token);
        }

        /// <summary>
        /// Get all records that need recomputation for a specific field.
        /// </summary>
        public IEnumerable<int> GetRecordsToRecompute(ModelHandle model, FieldHandle field)
        {
            if (!_toRecompute.TryGetValue(model.Token, out var recordFields))
            {
                yield break;
            }

            foreach (var (recordId, fields) in recordFields)
            {
                if (fields.Contains(field.Token))
                {
                    yield return recordId;
                }
            }
        }

        /// <summary>
        /// Get all fields that need recomputation for a specific record.
        /// </summary>
        public IEnumerable<FieldHandle> GetFieldsToRecompute(ModelHandle model, int recordId)
        {
            if (_toRecompute.TryGetValue(model.Token, out var recordFields) &&
                recordFields.TryGetValue(recordId, out var fields))
            {
                return fields.Select(t => new FieldHandle(t));
            }

            return Enumerable.Empty<FieldHandle>();
        }

        /// <summary>
        /// Clear the recompute flag for a specific field after computation.
        /// </summary>
        public void ClearRecompute(ModelHandle model, int recordId, FieldHandle field)
        {
            if (_toRecompute.TryGetValue(model.Token, out var recordFields) &&
                recordFields.TryGetValue(recordId, out var fields))
            {
                fields.Remove(field.Token);
                
                if (fields.Count == 0)
                {
                    recordFields.Remove(recordId);
                }
            }
        }

        /// <summary>
        /// Clear all recompute flags for a record.
        /// </summary>
        public void ClearRecord(ModelHandle model, int recordId)
        {
            if (_toRecompute.TryGetValue(model.Token, out var recordFields))
            {
                recordFields.Remove(recordId);
            }
        }

        /// <summary>
        /// Clear all recompute flags.
        /// </summary>
        public void ClearAll()
        {
            _toRecompute.Clear();
        }

        /// <summary>
        /// Check if there are any fields pending recomputation.
        /// </summary>
        public bool HasPendingRecompute => _toRecompute.Count > 0;

        /// <summary>
        /// Get all pending recomputation grouped by model.
        /// </summary>
        public IEnumerable<(int ModelToken, int RecordId, int FieldToken)> GetAllPendingRecompute()
        {
            foreach (var (modelToken, recordFields) in _toRecompute)
            {
                foreach (var (recordId, fieldTokens) in recordFields)
                {
                    foreach (var fieldToken in fieldTokens)
                    {
                        yield return (modelToken, recordId, fieldToken);
                    }
                }
            }
        }

        /// <summary>
        /// Get dependencies for a field (what computed fields depend on this field).
        /// </summary>
        public IEnumerable<(ModelHandle Model, FieldHandle Field)> GetDependents(ModelHandle model, FieldHandle field)
        {
            var key = (model.Token, field.Token);
            
            if (_dependencyGraph.TryGetValue(key, out var dependents))
            {
                return dependents.Select(d => (new ModelHandle(d.ModelToken), new FieldHandle(d.FieldToken)));
            }

            return Enumerable.Empty<(ModelHandle, FieldHandle)>();
        }

        /// <summary>
        /// Clear all registered dependencies.
        /// Typically called during registry rebuild.
        /// </summary>
        public void ClearDependencies()
        {
            _dependencyGraph.Clear();
        }
    }
}
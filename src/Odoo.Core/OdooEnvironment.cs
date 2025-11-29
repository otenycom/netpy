using System;
using System.Collections.Generic;
using System.Linq;
using Odoo.Core.Modules;
using Odoo.Core.Pipeline;

namespace Odoo.Core
{
    /// <summary>
    /// Concrete implementation of IEnvironment.
    /// Manages the execution context for ORM operations.
    /// <para>
    /// <b>THREAD SAFETY:</b> This class is NOT thread-safe.
    /// It is designed to be used as a scoped service (per request/transaction).
    /// Do not share instances across threads.
    /// </para>
    /// </summary>
    public class OdooEnvironment : IEnvironment
    {
        private readonly Dictionary<Type, Delegate> _recordFactories = new();
        private readonly ModelRegistry? _modelRegistry;
        private readonly PipelineRegistry _pipelineRegistry;

        /// <summary>
        /// Identity Map: Caches record instances to ensure reference equality.
        /// Key is (ModelToken, RecordId), Value is the unified wrapper instance.
        /// </summary>
        private readonly Dictionary<(int ModelToken, RecordId Id), IOdooRecord> _identityMap =
            new();

        /// <summary>
        /// Protection tracking: Field → Set of protected record IDs.
        /// Protected records can have computed field setters write directly to cache
        /// without triggering the Write pipeline (which would cause infinite recursion).
        /// Mirrors Odoo's env._protected pattern.
        /// </summary>
        private readonly Dictionary<FieldHandle, HashSet<RecordId>> _protected = new();

        public int UserId { get; }
        public IColumnarCache Columns { get; }
        public IPipelineBuilder Methods => _pipelineRegistry;
        public IdGenerator IdGenerator { get; }

        /// <summary>
        /// Tracks dirty fields across all models for this environment.
        /// Used by Flush() to determine what needs to be written.
        /// </summary>
        public DirtyTracker DirtyTracker { get; }

        /// <summary>
        /// Tracks computed fields that need recomputation.
        /// Used for the @api.depends pattern.
        /// </summary>
        public ComputeTracker ComputeTracker { get; }

        /// <summary>
        /// Fast access to compiled pipeline delegates.
        /// </summary>
        public TDelegate GetPipeline<TDelegate>(string model, string method)
            where TDelegate : Delegate
        {
            return _pipelineRegistry.GetPipeline<TDelegate>(model, method);
        }

        public OdooEnvironment(
            int userId,
            IColumnarCache? cache = null,
            ModelRegistry? modelRegistry = null,
            PipelineRegistry? pipelineRegistry = null,
            DirtyTracker? dirtyTracker = null,
            ComputeTracker? computeTracker = null
        )
        {
            UserId = userId;
            Columns = cache ?? new ColumnarValueCache();
            _modelRegistry = modelRegistry;
            _pipelineRegistry = pipelineRegistry ?? new PipelineRegistry();
            IdGenerator = new IdGenerator();
            DirtyTracker = dirtyTracker ?? new DirtyTracker();
            ComputeTracker = computeTracker ?? new ComputeTracker(_modelRegistry);
        }

        /// <summary>
        /// Get a record from the identity map, creating it if necessary.
        /// This ensures reference equality for the same (model, id) combination.
        /// </summary>
        /// <typeparam name="T">The interface type to retrieve the record as.</typeparam>
        /// <param name="modelName">The Odoo model name (e.g., "res.partner").</param>
        /// <param name="id">The record ID.</param>
        /// <returns>The record instance cast to T.</returns>
        /// <example>
        /// var partner1 = env.GetRecord&lt;IPartnerBase&gt;("res.partner", 1);
        /// var partner2 = env.GetRecord&lt;IPartnerSaleExtension&gt;("res.partner", 1);
        /// Assert.True(ReferenceEquals(partner1, partner2)); // Same instance!
        /// </example>
        public T GetRecord<T>(string modelName, RecordId id)
            where T : class, IOdooRecord
        {
            if (_modelRegistry == null)
                throw new InvalidOperationException("Model registry is not initialized");

            var schema = _modelRegistry.GetModel(modelName);
            if (schema == null)
                throw new KeyNotFoundException($"Model '{modelName}' not found");

            var key = (schema.Token.Token, id);

            if (_identityMap.TryGetValue(key, out var existing))
            {
                // Cache hit - return existing instance
                return (T)existing;
            }

            // Cache miss - create new instance using registered factory
            var factory = _modelRegistry.GetRecordFactory(modelName);
            var record = factory(this, id);

            _identityMap[key] = record;
            return (T)record;
        }

        /// <summary>
        /// Get a record by ID, inferring the model name from the interface's [OdooModel] attribute.
        /// </summary>
        public T GetRecord<T>(RecordId id)
            where T : class, IOdooRecord
        {
            var modelName = GetModelName<T>();
            return GetRecord<T>(modelName, id);
        }

        /// <summary>
        /// Register a record in the identity map.
        /// Called when creating new records to ensure reference equality.
        /// </summary>
        public void RegisterInIdentityMap(int modelToken, RecordId id, IOdooRecord record)
        {
            var key = (modelToken, id);
            _identityMap[key] = record;
        }

        /// <summary>
        /// Check if a record exists in the identity map.
        /// </summary>
        public bool TryGetFromIdentityMap(int modelToken, RecordId id, out IOdooRecord? record)
        {
            return _identityMap.TryGetValue((modelToken, id), out record);
        }

        /// <summary>
        /// Clear the identity map. Use with caution - invalidates all cached references.
        /// </summary>
        public void ClearIdentityMap()
        {
            _identityMap.Clear();
        }

        /// <summary>
        /// Get multiple records as a RecordSet with identity map support.
        /// </summary>
        /// <typeparam name="T">The interface type for the records.</typeparam>
        /// <param name="modelName">The Odoo model name.</param>
        /// <param name="ids">The record IDs.</param>
        /// <returns>A RecordSet of records.</returns>
        public RecordSet<T> GetRecords<T>(string modelName, RecordId[] ids)
            where T : class, IOdooRecord
        {
            return new RecordSet<T>(
                this,
                modelName,
                ids,
                (env, id) => ((OdooEnvironment)env).GetRecord<T>(modelName, id)
            );
        }

        /// <summary>
        /// Get multiple records as a RecordSet, inferring model name from interface.
        /// </summary>
        public RecordSet<T> GetRecords<T>(RecordId[] ids)
            where T : class, IOdooRecord
        {
            var modelName = GetModelName<T>();
            return GetRecords<T>(modelName, ids);
        }

        /// <summary>
        /// Get a record by model token and ID.
        /// Used by RecordHandle.As&lt;T&gt;() for identity map lookups.
        /// </summary>
        public T GetRecordByToken<T>(int modelToken, RecordId id)
            where T : class, IOdooRecord
        {
            if (_modelRegistry == null)
                throw new InvalidOperationException("Model registry is not initialized");

            var key = (modelToken, id);

            if (_identityMap.TryGetValue(key, out var existing))
            {
                return (T)existing;
            }

            // Find the model name from token
            foreach (var schema in _modelRegistry.GetAllModels())
            {
                if (schema.Token.Token == modelToken)
                {
                    var factory = _modelRegistry.GetRecordFactory(schema.ModelName);
                    var record = factory(this, id);
                    _identityMap[key] = record;
                    return (T)record;
                }
            }

            throw new KeyNotFoundException($"No model found with token {modelToken}");
        }

        /// <summary>
        /// Access a model by name (Pythonic syntax).
        /// Example: env["res.partner"]
        /// </summary>
        public ModelProxy this[string modelName]
        {
            get
            {
                if (_modelRegistry == null)
                    throw new InvalidOperationException("Model registry is not initialized");

                var schema = _modelRegistry.GetModel(modelName);
                if (schema == null)
                    throw new KeyNotFoundException($"Model '{modelName}' not found");

                return new ModelProxy(this, modelName, schema);
            }
        }

        public RecordSet<T> GetModel<T>()
            where T : IOdooRecord
        {
            return CreateRecordSet<T>(Array.Empty<RecordId>());
        }

        public RecordSet<T> CreateRecordSet<T>(RecordId[] ids)
            where T : IOdooRecord
        {
            var factory = GetOrCreateFactory<T>();
            var modelName = GetModelName<T>();
            return new RecordSet<T>(this, modelName, ids, factory);
        }

        /// <summary>
        /// Register a custom factory for creating record instances.
        /// This is used by the generated code to register record constructors.
        /// </summary>
        public void RegisterFactory<T>(string modelName, Func<IEnvironment, RecordId, T> factory)
            where T : IOdooRecord
        {
            _recordFactories[typeof(T)] = factory;
        }

        private Func<IEnvironment, RecordId, T> GetOrCreateFactory<T>()
            where T : IOdooRecord
        {
            if (_recordFactories.TryGetValue(typeof(T), out var factory))
            {
                return (Func<IEnvironment, RecordId, T>)factory;
            }

            // Try to get from registry if available
            var modelName = GetModelName<T>();
            if (_modelRegistry != null)
            {
                try
                {
                    var registryFactory = _modelRegistry.GetRecordFactory(modelName);
                    // We need to cast the generic IOdooRecord return type to T
                    // This assumes the factory actually produces T, which it should if T is the interface
                    return (env, id) => (T)registryFactory(env, id);
                }
                catch (KeyNotFoundException)
                {
                    // Fall through to error
                }
            }

            // Default factory - this will be replaced by generated code
            throw new InvalidOperationException(
                $"No factory registered for type {typeof(T).Name}. "
                    + "Ensure the source generator has run and generated the necessary code."
            );
        }

        private string GetModelName<T>()
            where T : IOdooRecord
        {
            // Extract model name from OdooModel attribute
            var type = typeof(T);
            var attr = type.GetCustomAttributes(typeof(OdooModelAttribute), true);

            if (attr.Length > 0 && attr[0] is OdooModelAttribute modelAttr)
            {
                return modelAttr.ModelName;
            }

            // Fallback to type name
            return type.Name.ToLower().Replace("i", "");
        }

        /// <summary>
        /// Create a new environment with a different user.
        /// Shares the same caches, dirty tracker, and compute tracker.
        /// <para>
        /// <b>WARNING:</b> The returned environment shares the same non-thread-safe cache.
        /// Do not use the new environment concurrently with the original one.
        /// </para>
        /// </summary>
        public OdooEnvironment WithUser(int userId)
        {
            return new OdooEnvironment(
                userId,
                Columns,
                _modelRegistry,
                _pipelineRegistry,
                DirtyTracker,
                ComputeTracker
            );
        }

        /// <summary>
        /// Create a copy of the environment with fresh caches.
        /// </summary>
        public OdooEnvironment WithNewCache()
        {
            return new OdooEnvironment(
                UserId,
                new ColumnarValueCache(),
                _modelRegistry,
                _pipelineRegistry
            );
        }

        // --- Flush and Recompute Operations ---

        /// <summary>
        /// Flush all pending writes to the database.
        /// This method:
        /// 1. Recomputes all pending computed fields
        /// 2. Groups dirty records by model
        /// 3. Calls the write pipeline for each model with dirty records
        /// 4. Clears dirty tracking
        /// <para>
        /// Mirrors Odoo's Environment.flush_all() behavior.
        /// </para>
        /// </summary>
        public void Flush()
        {
            // Step 1: Recompute all pending computed fields first
            RecomputePending();

            // Step 2: Get all dirty models
            var dirtyModels = DirtyTracker.GetDirtyModels();

            foreach (var modelToken in dirtyModels)
            {
                FlushModel(new ModelHandle(modelToken));
            }

            // Step 3: Clear dirty tracking
            DirtyTracker.ClearAll();
        }

        /// <summary>
        /// Flush a specific model's dirty records to the database.
        /// </summary>
        public void FlushModel(ModelHandle model)
        {
            if (_modelRegistry == null)
                return;

            var schema = _modelRegistry
                .GetAllModels()
                .FirstOrDefault(s => s.Token.Token == model.Token);

            if (schema == null)
                return;

            // Get all dirty records for this model
            var dirtyRecordIds = DirtyTracker.GetDirtyRecordIds(model).ToArray();

            if (dirtyRecordIds.Length == 0)
                return;

            // Build values dictionary for each record
            // In the future, this will be optimized to batch by field values
            foreach (var recordId in dirtyRecordIds)
            {
                var dirtyFields = DirtyTracker.GetDirtyFields(model, recordId);
                var values = new Dictionary<string, object?>();

                foreach (var fieldToken in dirtyFields)
                {
                    // Find field name from token
                    var fieldSchema = schema.Fields.Values.FirstOrDefault(f =>
                        f.Token.Token == fieldToken.Token
                    );

                    if (fieldSchema != null && !fieldSchema.IsComputed)
                    {
                        // Get current value from cache (using object boxing for now)
                        // In the future, this will use typed access
                        // TODO: Implement typed value extraction
                    }
                }

                // TODO: Call write pipeline when implemented
                // var writePipeline = GetPipeline<WriteDelegate>(schema.ModelName, "write");
                // writePipeline(new[] { recordId }, values);
            }
        }

        /// <summary>
        /// Recompute all pending computed fields.
        /// Called automatically by Flush() before writing to database.
        /// </summary>
        public void RecomputePending()
        {
            if (_modelRegistry == null)
                return;

            while (ComputeTracker.HasPendingRecompute)
            {
                var pending = ComputeTracker.GetAllPendingRecompute().ToList();

                foreach (var (modelToken, recordId, fieldToken) in pending)
                {
                    var schema = _modelRegistry
                        .GetAllModels()
                        .FirstOrDefault(s => s.Token.Token == modelToken);

                    if (schema == null)
                        continue;

                    var fieldSchema = schema.Fields.Values.FirstOrDefault(f =>
                        f.Token.Token == fieldToken
                    );

                    if (fieldSchema == null || !fieldSchema.IsComputed)
                        continue;

                    // TODO: Call the compute method when pipelines are implemented
                    // var computePipeline = GetPipeline<ComputeDelegate>(schema.ModelName, fieldSchema.ComputeMethodName);
                    // computePipeline(new[] { recordId });

                    // Clear the recompute flag
                    ComputeTracker.ClearRecompute(
                        new ModelHandle(modelToken),
                        recordId,
                        new FieldHandle(fieldToken)
                    );
                }
            }
        }

        /// <summary>
        /// Mark a field as modified and trigger recomputation of dependent fields.
        /// Called by property setters for stored fields.
        /// </summary>
        /// <param name="model">The model handle</param>
        /// <param name="recordId">The record ID</param>
        /// <param name="field">The field that was modified</param>
        public void Modified(ModelHandle model, RecordId recordId, FieldHandle field)
        {
            DirtyTracker.MarkDirty(model, recordId, field);
            ComputeTracker.Modified(model, recordId, field);
        }

        /// <summary>
        /// Mark multiple fields as modified on a record.
        /// </summary>
        public void Modified(ModelHandle model, RecordId recordId, IEnumerable<FieldHandle> fields)
        {
            foreach (var field in fields)
            {
                DirtyTracker.MarkDirty(model, recordId, field);
            }
            ComputeTracker.Modified(model, recordId, fields);
        }

        // --- Computed Field Helpers ---

        /// <summary>
        /// Set a computed field value directly to the cache without triggering the Write pipeline.
        /// This method is used by compute methods to store their results.
        /// <para>
        /// Unlike normal property setters (which go through the Write pipeline), this method:
        /// - Writes directly to the cache
        /// - Does NOT mark the field as dirty (computed fields are recomputed, not written to DB directly from cache)
        /// - Does NOT trigger Modified() (to avoid cascading recomputation)
        /// - Clears the recompute flag for this field
        /// </para>
        /// </summary>
        /// <typeparam name="T">The type of the field value</typeparam>
        /// <param name="model">The model handle</param>
        /// <param name="recordId">The record ID</param>
        /// <param name="field">The field handle</param>
        /// <param name="value">The computed value to set</param>
        public void SetComputedValue<T>(
            ModelHandle model,
            RecordId recordId,
            FieldHandle field,
            T value
        )
        {
            // Direct cache write - bypasses Write pipeline
            Columns.SetValue(model, recordId, field, value);

            // Clear the recompute flag since we just computed it
            ComputeTracker.ClearRecompute(model, recordId, field);
        }

        // --- Protection Mechanism for Computed Fields ---

        /// <summary>
        /// Check if a record is currently protected for a specific field.
        /// During protection, setters bypass the Write pipeline and write directly to cache.
        /// This prevents infinite recursion when compute methods set computed field values.
        /// <para>
        /// Mirrors Odoo's check: <c>record_id in records.env._protected.get(self, ())</c>
        /// </para>
        /// </summary>
        /// <param name="field">The field handle</param>
        /// <param name="recordId">The record ID</param>
        /// <returns>True if the record is protected for this field, false otherwise</returns>
        public bool IsProtected(FieldHandle field, RecordId recordId)
        {
            return _protected.TryGetValue(field, out var ids) && ids.Contains(recordId);
        }

        /// <summary>
        /// Protect records for specific fields during computation.
        /// Returns an IDisposable scope that removes protection on dispose.
        /// <para>
        /// Mirrors Odoo's <c>env.protecting(fields, records)</c> context manager.
        /// Use this to wrap compute method execution so that computed field setters
        /// can write directly to cache without triggering the Write pipeline.
        /// </para>
        /// </summary>
        /// <param name="fields">Fields to protect</param>
        /// <param name="recordIds">Record IDs to protect</param>
        /// <returns>Disposable scope that clears protection when disposed</returns>
        /// <example>
        /// <code>
        /// using (odooEnv.Protecting(new[] { ModelSchema.ResPartner.DisplayName }, self.Ids))
        /// {
        ///     // Inside this block, partner.DisplayName = value writes directly to cache
        ///     foreach (var partner in self)
        ///     {
        ///         partner.DisplayName = ComputeValue(partner);
        ///     }
        /// }
        /// </code>
        /// </example>
        public IDisposable Protecting(
            IEnumerable<FieldHandle> fields,
            IEnumerable<RecordId> recordIds
        )
        {
            var fieldList = fields.ToList();
            var idSet = recordIds.ToHashSet();

            foreach (var field in fieldList)
            {
                if (!_protected.TryGetValue(field, out var ids))
                {
                    ids = new HashSet<RecordId>();
                    _protected[field] = ids;
                }
                ids.UnionWith(idSet);
            }

            return new ProtectionScope(this, fieldList, idSet);
        }

        /// <summary>
        /// Remove protection for specific fields and record IDs.
        /// Called by ProtectionScope.Dispose().
        /// </summary>
        private void ClearProtection(
            IEnumerable<FieldHandle> fields,
            IEnumerable<RecordId> recordIds
        )
        {
            foreach (var field in fields)
            {
                if (_protected.TryGetValue(field, out var ids))
                {
                    foreach (var id in recordIds)
                    {
                        ids.Remove(id);
                    }

                    // Clean up empty sets to prevent memory growth
                    if (ids.Count == 0)
                    {
                        _protected.Remove(field);
                    }
                }
            }
        }

        /// <summary>
        /// Disposable scope that removes field protection when disposed.
        /// Created by <see cref="Protecting"/> method.
        /// </summary>
        private sealed class ProtectionScope : IDisposable
        {
            private readonly OdooEnvironment _env;
            private readonly List<FieldHandle> _fields;
            private readonly HashSet<RecordId> _recordIds;
            private bool _disposed;

            public ProtectionScope(
                OdooEnvironment env,
                List<FieldHandle> fields,
                HashSet<RecordId> recordIds
            )
            {
                _env = env;
                _fields = fields;
                _recordIds = recordIds;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _env.ClearProtection(_fields, _recordIds);
                }
            }
        }
    }

    /// <summary>
    /// Extension methods for IEnvironment to support computed field operations.
    /// </summary>
    public static class EnvironmentComputeExtensions
    {
        /// <summary>
        /// Set a computed field value directly to the cache without triggering the Write pipeline.
        /// Extension method version for use in compute methods.
        /// </summary>
        /// <typeparam name="T">The type of the field value</typeparam>
        /// <param name="env">The environment</param>
        /// <param name="model">The model handle</param>
        /// <param name="recordId">The record ID</param>
        /// <param name="field">The field handle</param>
        /// <param name="value">The computed value to set</param>
        /// <exception cref="InvalidOperationException">If env is not an OdooEnvironment</exception>
        public static void SetComputedValue<T>(
            this IEnvironment env,
            ModelHandle model,
            RecordId recordId,
            FieldHandle field,
            T value
        )
        {
            if (env is OdooEnvironment odooEnv)
            {
                odooEnv.SetComputedValue(model, recordId, field, value);
            }
            else
            {
                // Fallback: just write to cache (won't clear recompute flag)
                env.Columns.SetValue(model, recordId, field, value);
            }
        }
    }
}

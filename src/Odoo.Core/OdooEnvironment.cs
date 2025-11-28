using System;
using System.Collections.Generic;
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
        private readonly Dictionary<(int ModelToken, int Id), IOdooRecord> _identityMap = new();

        public int UserId { get; }
        public IColumnarCache Columns { get; }
        public IPipelineBuilder Methods => _pipelineRegistry;
        public IdGenerator IdGenerator { get; }

        /// <summary>
        /// Fast access to compiled pipeline delegates.
        /// </summary>
        public TDelegate GetPipeline<TDelegate>(string model, string method) where TDelegate : Delegate
        {
            return _pipelineRegistry.GetPipeline<TDelegate>(model, method);
        }

        public OdooEnvironment(int userId, IColumnarCache? cache = null, ModelRegistry? modelRegistry = null, PipelineRegistry? pipelineRegistry = null)
        {
            UserId = userId;
            Columns = cache ?? new ColumnarValueCache();
            _modelRegistry = modelRegistry;
            _pipelineRegistry = pipelineRegistry ?? new PipelineRegistry();
            IdGenerator = new IdGenerator();
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
        public T GetRecord<T>(string modelName, int id) where T : class, IOdooRecord
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
        public T GetRecord<T>(int id) where T : class, IOdooRecord
        {
            var modelName = GetModelName<T>();
            return GetRecord<T>(modelName, id);
        }
        
        /// <summary>
        /// Register a record in the identity map.
        /// Called when creating new records to ensure reference equality.
        /// </summary>
        public void RegisterInIdentityMap(int modelToken, int id, IOdooRecord record)
        {
            var key = (modelToken, id);
            _identityMap[key] = record;
        }
        
        /// <summary>
        /// Check if a record exists in the identity map.
        /// </summary>
        public bool TryGetFromIdentityMap(int modelToken, int id, out IOdooRecord? record)
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
        public RecordSet<T> GetRecords<T>(string modelName, int[] ids) where T : class, IOdooRecord
        {
            return new RecordSet<T>(
                this,
                modelName,
                ids,
                (env, id) => ((OdooEnvironment)env).GetRecord<T>(modelName, id));
        }
        
        /// <summary>
        /// Get multiple records as a RecordSet, inferring model name from interface.
        /// </summary>
        public RecordSet<T> GetRecords<T>(int[] ids) where T : class, IOdooRecord
        {
            var modelName = GetModelName<T>();
            return GetRecords<T>(modelName, ids);
        }
        
        /// <summary>
        /// Get a record by model token and ID.
        /// Used by RecordHandle.As&lt;T&gt;() for identity map lookups.
        /// </summary>
        public T GetRecordByToken<T>(int modelToken, int id) where T : class, IOdooRecord
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

        public RecordSet<T> GetModel<T>() where T : IOdooRecord
        {
            return CreateRecordSet<T>(Array.Empty<int>());
        }

        public RecordSet<T> CreateRecordSet<T>(int[] ids) where T : IOdooRecord
        {
            var factory = GetOrCreateFactory<T>();
            var modelName = GetModelName<T>();
            return new RecordSet<T>(this, modelName, ids, factory);
        }

        /// <summary>
        /// Register a custom factory for creating record instances.
        /// This is used by the generated code to register record constructors.
        /// </summary>
        public void RegisterFactory<T>(string modelName, Func<IEnvironment, int, T> factory)
            where T : IOdooRecord
        {
            _recordFactories[typeof(T)] = factory;
        }

        private Func<IEnvironment, int, T> GetOrCreateFactory<T>() where T : IOdooRecord
        {
            if (_recordFactories.TryGetValue(typeof(T), out var factory))
            {
                return (Func<IEnvironment, int, T>)factory;
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
                $"No factory registered for type {typeof(T).Name}. " +
                "Ensure the source generator has run and generated the necessary code.");
        }

        private string GetModelName<T>() where T : IOdooRecord
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
        /// Shares the same caches.
        /// <para>
        /// <b>WARNING:</b> The returned environment shares the same non-thread-safe cache.
        /// Do not use the new environment concurrently with the original one.
        /// </para>
        /// </summary>
        public OdooEnvironment WithUser(int userId)
        {
            return new OdooEnvironment(userId, Columns, _modelRegistry, _pipelineRegistry);
        }

        /// <summary>
        /// Create a copy of the environment with fresh caches.
        /// </summary>
        public OdooEnvironment WithNewCache()
        {
            return new OdooEnvironment(UserId, new ColumnarValueCache(), _modelRegistry, _pipelineRegistry);
        }
    }
}
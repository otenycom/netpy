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
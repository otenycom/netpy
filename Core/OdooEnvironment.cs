using System;
using System.Collections.Generic;

namespace Odoo.Core
{
    /// <summary>
    /// Concrete implementation of IEnvironment.
    /// Manages the execution context for ORM operations.
    /// </summary>
    public class OdooEnvironment : IEnvironment
    {
        private readonly Dictionary<Type, Delegate> _recordFactories = new();

        public int UserId { get; }
        public IValueCache Cache { get; }

        public OdooEnvironment(int userId, IValueCache? cache = null)
        {
            UserId = userId;
            Cache = cache ?? new SimpleValueCache();
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
        /// Shares the same cache.
        /// </summary>
        public OdooEnvironment WithUser(int userId)
        {
            return new OdooEnvironment(userId, Cache);
        }

        /// <summary>
        /// Create a copy of the environment with a fresh cache.
        /// </summary>
        public OdooEnvironment WithNewCache()
        {
            return new OdooEnvironment(UserId, new SimpleValueCache());
        }
    }
}
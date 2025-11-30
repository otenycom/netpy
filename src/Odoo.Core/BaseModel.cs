using System.Collections.Generic;
using Odoo.Core.Pipeline;

namespace Odoo.Core
{
    /// <summary>
    /// Base implementations for ORM methods (browse, write, create).
    /// Contains the single source of truth for these operations.
    /// Methods are registered in the pipeline system and can be overridden by addons.
    ///
    /// These methods are registered with model name "model" which corresponds to
    /// the IModel interface ([OdooModel("model")]). All model interfaces that inherit
    /// from IModel automatically get these base implementations through pipeline inheritance.
    /// </summary>
    public static class BaseModel
    {
        /// <summary>
        /// Base browse implementation - returns records by IDs.
        /// Registered as the base handler in the "browse" pipeline.
        /// <para>
        /// This method:
        /// 1. Gets the model token from the model name
        /// 2. Creates a RecordSetWrapper containing the specified IDs
        /// </para>
        /// </summary>
        /// <param name="env">The environment</param>
        /// <param name="modelName">The model name (e.g., "res.partner")</param>
        /// <param name="ids">The record IDs to browse</param>
        /// <returns>RecordSetWrapper for the specified IDs</returns>
        [OdooLogic("model", "browse")]
        public static object Browse_Base(
            OdooEnvironment env,
            string modelName,
            IEnumerable<long> ids
        )
        {
            var recordIds = new List<RecordId>();
            foreach (var id in ids)
            {
                recordIds.Add(new RecordId((int)id));
            }

            // Return a simple object containing the IDs for Python to wrap
            return new BrowseResult(env, modelName, recordIds.ToArray());
        }

        /// <summary>
        /// Result type for browse operation, used as an intermediate result.
        /// </summary>
        public class BrowseResult
        {
            public OdooEnvironment Env { get; }
            public string ModelName { get; }
            public RecordId[] Ids { get; }

            public BrowseResult(OdooEnvironment env, string modelName, RecordId[] ids)
            {
                Env = env;
                ModelName = modelName;
                Ids = ids;
            }
        }

        /// <summary>
        /// Base write implementation - replaces per-model generated Write methods.
        /// Registered as the base handler in the "write" pipeline.
        /// <para>
        /// This method:
        /// 1. Gets the values handler for the model
        /// 2. Applies values to the columnar cache
        /// 3. Marks fields as dirty for flush
        /// 4. Triggers Modified for computed field recomputation
        /// </para>
        /// </summary>
        /// <param name="handle">The record handle (env, id, model token)</param>
        /// <param name="vals">The values to write</param>
        [OdooLogic("model", "write")]
        public static void Write_Base(RecordHandle handle, IRecordValues vals)
        {
            if (handle.Env is not OdooEnvironment env)
                throw new System.InvalidOperationException("Write_Base requires OdooEnvironment");

            var handler = env.GetValuesHandler(handle.Model);
            handler.ApplyToCache(vals, env.Columns, handle.Model, handle.Id);
            handler.MarkDirty(vals, env.Columns, handle.Model, handle.Id);
            handler.TriggerModified(vals, env, handle.Model, handle.Id);
        }

        /// <summary>
        /// Write implementation for dictionary values (Python interop).
        /// Used when the caller has a raw dictionary instead of typed IRecordValues.
        /// </summary>
        /// <param name="env">The environment</param>
        /// <param name="modelName">The model name (e.g., "res.partner")</param>
        /// <param name="ids">The record IDs to write to</param>
        /// <param name="vals">The values dictionary</param>
        [OdooLogic("model", "write_dict")]
        public static bool WriteDict_Base(
            OdooEnvironment env,
            string modelName,
            IEnumerable<long> ids,
            IDictionary<string, object> vals
        )
        {
            var modelToken = env.GetModelToken(modelName);
            var handler = env.GetValuesHandler(modelName);

            // Convert dictionary to IRecordValues using the handler
            var typedVals = handler.FromDictionary(
                new Dictionary<string, object?>(
                    vals.Select(kvp => new KeyValuePair<string, object?>(kvp.Key, kvp.Value))
                )
            );

            foreach (var id in ids)
            {
                var recordId = new RecordId((int)id);
                handler.ApplyToCache(typedVals, env.Columns, modelToken, recordId);
                handler.MarkDirty(typedVals, env.Columns, modelToken, recordId);
                handler.TriggerModified(typedVals, env, modelToken, recordId);
            }

            return true;
        }

        /// <summary>
        /// Base create implementation - replaces per-model generated Create methods.
        /// Registered as the base handler in the "create" pipeline.
        /// <para>
        /// This method:
        /// 1. Generates a new ID
        /// 2. Gets the values handler for the model
        /// 3. Applies values to the columnar cache
        /// 4. Creates a record wrapper using the factory
        /// 5. Registers in identity map
        /// 6. Marks fields as dirty and triggers Modified
        /// 7. Returns the new record
        /// </para>
        /// </summary>
        /// <param name="env">The environment</param>
        /// <param name="modelName">The model name (e.g., "res.partner")</param>
        /// <param name="vals">The initial values</param>
        /// <returns>The newly created record</returns>
        [OdooLogic("model", "create")]
        public static IOdooRecord Create_Base(
            OdooEnvironment env,
            string modelName,
            IRecordValues vals
        )
        {
            var modelToken = env.GetModelToken(modelName);
            var newId = env.IdGenerator.NextId(modelName);

            var handler = env.GetValuesHandler(modelName);
            handler.ApplyToCache(vals, env.Columns, modelToken, newId);

            var record = env.CreateRecord(modelToken, newId);
            env.RegisterInIdentityMap(modelToken, newId, record);

            handler.MarkDirty(vals, env.Columns, modelToken, newId);
            handler.TriggerModified(vals, env, modelToken, newId);

            return record;
        }
    }
}

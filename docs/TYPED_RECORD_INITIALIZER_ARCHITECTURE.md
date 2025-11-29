# Typed Record Initializer Architecture

## Overview

This document outlines the architecture for implementing high-performance typed record creation in the Odoo clone project. The focus is on:

1. **Maximum performance** through generated typed messages
2. **Ease of use** with C# object initializer syntax
3. **Mutability** for addon composition (hooks can modify values)
4. **Batch operations** aligned with Odoo's patterns (create/write multiple records)
5. **Python interop** via dictionary conversion (higher-level, not core path)

## Current State

### Existing Tests
```csharp
// Test 1: Typed Values struct (current)
var partner = env.Create(new ResPartnerValues { Name = "Alice" });

// Test 2: Dictionary-based (Python interop)
var company = ResPartnerPipelines.Create(
    env,
    new() { { "name", "Acme Corp" }, { "is_company", true } }
);
```

### Current Limitations
- `ResPartnerValues` uses nullable types (`string?`) which can't distinguish "not set" vs "explicitly null"
- Dictionary path involves boxing and string lookups
- No support for addon mutation of values during creation
- No batch create/write support

## Proposed Architecture

### Core Design Principle

```
[User Code]                         [Typed Fast Path]           [Storage]
     │                                   │                          │
     ▼                                   ▼                          │
ResPartnerValues ─────────────► RecordValueField<T>.IsSet ────► Cache Write
(object initializer)             (no reflection)                (columnar)
     │                                   │                          │
     │                                   ▼                          │
     │                             Addon Mutation                   │
     │                             (in-place modify)                │
     │                                   │                          │
     ▼                                   ▼                          │
IEnumerable<TValues> ─────────► Batch Processing ─────────► Bulk Cache Write
(batch create/write)             (loop over values)
     │                                   │                          │
     ▼                                   ▼                          │
Dictionary ◄──────────────────── FromDictionary() ◄──────── Python Interop
(conversion only)                 (higher level)
```

---

## Part 1: Core Infrastructure (`Odoo.Core`)

### 1.1 RecordValueField<T> - The Settable Field Wrapper

**File:** `src/Odoo.Core/RecordValueField.cs`

```csharp
namespace Odoo.Core
{
    /// <summary>
    /// Wrapper for field values that tracks whether a value was explicitly set.
    /// Used in record values classes for record creation/updates.
    ///
    /// Key features:
    /// - Mutable (reference type) for addon modification
    /// - Implicit conversion from T for clean syntax
    /// - No reflection needed to check IsSet
    /// </summary>
    public sealed class RecordValueField<T>
    {
        public T? Value { get; set; }
        public bool IsSet { get; private set; }

        public RecordValueField() { }

        public RecordValueField(T value)
        {
            Value = value;
            IsSet = true;
        }

        /// <summary>
        /// Enables: field = value (instead of field = new RecordValueField<T>(value))
        /// </summary>
        public static implicit operator RecordValueField<T>(T value) => new(value);

        /// <summary>
        /// Reset to unset state
        /// </summary>
        public void Clear()
        {
            Value = default;
            IsSet = false;
        }

        /// <summary>
        /// Explicitly set value (updates IsSet flag)
        /// </summary>
        public void Set(T value)
        {
            Value = value;
            IsSet = true;
        }
    }
}
```

### 1.2 IRecordValues - Base Interface for All Record Values

**File:** `src/Odoo.Core/IRecordValues.cs`

```csharp
namespace Odoo.Core
{
    /// <summary>
    /// Base interface for all generated record value classes.
    /// Enables generic handling in pipelines and batch operations.
    /// </summary>
    public interface IRecordValues
    {
        /// <summary>
        /// Model name this values object is for (e.g., "res.partner")
        /// </summary>
        string ModelName { get; }
        
        /// <summary>
        /// Convert to dictionary for Python interop or legacy code.
        /// Only includes fields where IsSet == true.
        /// </summary>
        Dictionary<string, object?> ToDictionary();
        
        /// <summary>
        /// Get list of field names that are set.
        /// </summary>
        IReadOnlyList<string> GetSetFields();
    }
    
    /// <summary>
    /// Typed interface for record values with model type information.
    /// Enables type-safe batch operations.
    /// </summary>
    /// <typeparam name="TRecord">The record type this values class creates</typeparam>
    public interface IRecordValues<TRecord> : IRecordValues
        where TRecord : IOdooRecord
    {
    }
}
```

### 1.3 IRecordValuesHandler - For Typed Pipeline Processing

**File:** `src/Odoo.Core/IRecordValuesHandler.cs`

```csharp
namespace Odoo.Core
{
    /// <summary>
    /// Handler interface for processing typed values without reflection.
    /// Generated for each model to enable fast field iteration.
    /// </summary>
    public interface IRecordValuesHandler<TValues> where TValues : IRecordValues
    {
        /// <summary>
        /// Apply set fields to cache for a single record.
        /// Generated implementation switches on IsSet checks - no reflection.
        /// </summary>
        void ApplyToCache(TValues values, IColumnarCache cache, ModelHandle model, int recordId);
        
        /// <summary>
        /// Apply set fields to cache for multiple records (batch).
        /// Each values instance is applied to its corresponding recordId.
        /// </summary>
        void ApplyToCacheBatch(
            IEnumerable<TValues> valuesCollection,
            IColumnarCache cache,
            ModelHandle model,
            int[] recordIds);
        
        /// <summary>
        /// Apply same values to multiple records (bulk write pattern).
        /// </summary>
        void ApplyToCacheBulk(
            TValues values,
            IColumnarCache cache,
            ModelHandle model,
            int[] recordIds);
        
        /// <summary>
        /// Invoke triggers for modified fields (computed field dependencies).
        /// </summary>
        void TriggerModified(TValues values, OdooEnvironment env, ModelHandle model, int recordId);
        
        /// <summary>
        /// Invoke triggers for batch of records.
        /// </summary>
        void TriggerModifiedBatch(TValues values, OdooEnvironment env, ModelHandle model, int[] recordIds);
    }
}
```

---

## Part 2: Generator Updates (`Odoo.SourceGenerator`)

### 2.1 Generated Values Class (Replaces Values Struct)

**Generated:** `{ClassName}Values.g.cs`

```csharp
// <auto-generated/>
#nullable enable

using System;
using System.Collections.Generic;
using Odoo.Core;

namespace Odoo.Generated.OdooBase
{
    /// <summary>
    /// Typed values for creating/updating res.partner records.
    /// Use with object initializer syntax:
    ///
    ///   new ResPartnerValues { Name = "Alice", IsCompany = true }
    ///
    /// Fields are tracked via RecordValueField<T>.IsSet for efficient processing.
    /// Addons can mutate values in-place during pipeline execution.
    ///
    /// Supports batch operations:
    ///   env.Create(new[] { values1, values2, values3 }) → RecordSet
    /// </summary>
    public sealed class ResPartnerValues : IRecordValues<IPartnerBase>
    {
        // ===== Field definitions =====
        
        /// <summary>Field: name</summary>
        public RecordValueField<string> Name { get; set; } = new();
        
        /// <summary>Field: is_company</summary>
        public RecordValueField<bool> IsCompany { get; set; } = new();
        
        /// <summary>Field: display_name (computed)</summary>
        public RecordValueField<string> DisplayName { get; set; } = new();

        // ===== IRecordValues implementation =====
        
        public string ModelName => "res.partner";
        
        public Dictionary<string, object?> ToDictionary()
        {
            var dict = new Dictionary<string, object?>();
            if (Name.IsSet) dict["name"] = Name.Value;
            if (IsCompany.IsSet) dict["is_company"] = IsCompany.Value;
            if (DisplayName.IsSet) dict["display_name"] = DisplayName.Value;
            return dict;
        }
        
        public IReadOnlyList<string> GetSetFields()
        {
            var fields = new List<string>();
            if (Name.IsSet) fields.Add("name");
            if (IsCompany.IsSet) fields.Add("is_company");
            if (DisplayName.IsSet) fields.Add("display_name");
            return fields;
        }
        
        // ===== Factory methods =====
        
        /// <summary>
        /// Create from dictionary (for Python interop)
        /// </summary>
        public static ResPartnerValues FromDictionary(Dictionary<string, object?> dict)
        {
            var values = new ResPartnerValues();
            
            if (dict.TryGetValue("name", out var name) && name is string nameVal)
                values.Name = nameVal;
                
            if (dict.TryGetValue("is_company", out var isCompany) && isCompany is bool isCompanyVal)
                values.IsCompany = isCompanyVal;
                
            if (dict.TryGetValue("display_name", out var displayName) && displayName is string displayNameVal)
                values.DisplayName = displayNameVal;
                
            return values;
        }
        
        /// <summary>
        /// Create multiple from dictionaries (for Python interop batch)
        /// </summary>
        public static IEnumerable<ResPartnerValues> FromDictionaries(
            IEnumerable<Dictionary<string, object?>> dicts)
        {
            foreach (var dict in dicts)
            {
                yield return FromDictionary(dict);
            }
        }
    }
}
```

### 2.2 Generated Values Handler (Fast Cache Application)

**Generated:** `{ClassName}ValuesHandler.g.cs`

```csharp
// <auto-generated/>
#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Odoo.Core;

namespace Odoo.Generated.OdooBase
{
    /// <summary>
    /// Fast handler for ResPartnerValues - no reflection, direct cache writes.
    /// Supports both single and batch operations.
    /// </summary>
    public sealed class ResPartnerValuesHandler : IRecordValuesHandler<ResPartnerValues>
    {
        public static readonly ResPartnerValuesHandler Instance = new();
        
        private ResPartnerValuesHandler() { }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ApplyToCache(
            ResPartnerValues values,
            IColumnarCache cache,
            ModelHandle model,
            int recordId)
        {
            // Direct IsSet checks - no reflection, no iteration
            if (values.Name.IsSet)
            {
                cache.SetValue(model, recordId, ModelSchema.ResPartner.Name, values.Name.Value!);
            }
            
            if (values.IsCompany.IsSet)
            {
                cache.SetValue(model, recordId, ModelSchema.ResPartner.IsCompany, values.IsCompany.Value);
            }
            
            if (values.DisplayName.IsSet)
            {
                cache.SetValue(model, recordId, ModelSchema.ResPartner.DisplayName, values.DisplayName.Value!);
            }
        }
        
        /// <summary>
        /// Apply different values to multiple records (batch create).
        /// Each values instance maps to its corresponding recordId.
        /// </summary>
        public void ApplyToCacheBatch(
            IEnumerable<ResPartnerValues> valuesCollection,
            IColumnarCache cache,
            ModelHandle model,
            int[] recordIds)
        {
            int i = 0;
            foreach (var values in valuesCollection)
            {
                if (i >= recordIds.Length)
                    throw new ArgumentException("More values than record IDs provided");
                    
                ApplyToCache(values, cache, model, recordIds[i]);
                i++;
            }
        }
        
        /// <summary>
        /// Apply same values to multiple records (bulk write).
        /// Odoo pattern: records.write(vals) applies vals to all records.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ApplyToCacheBulk(
            ResPartnerValues values,
            IColumnarCache cache,
            ModelHandle model,
            int[] recordIds)
        {
            // For bulk operations, we could optimize with columnar writes
            // For now, iterate and apply same values to each record
            foreach (var recordId in recordIds)
            {
                ApplyToCache(values, cache, model, recordId);
            }
        }
        
        public void TriggerModified(
            ResPartnerValues values,
            OdooEnvironment env,
            ModelHandle model,
            int recordId)
        {
            // Only trigger for non-computed stored fields
            if (values.Name.IsSet)
            {
                env.Modified(model, recordId, ModelSchema.ResPartner.Name);
            }
            
            if (values.IsCompany.IsSet)
            {
                env.Modified(model, recordId, ModelSchema.ResPartner.IsCompany);
            }
            // Note: DisplayName is computed, don't trigger Modified for it
        }
        
        /// <summary>
        /// Trigger modified for batch of records with same field changes.
        /// </summary>
        public void TriggerModifiedBatch(
            ResPartnerValues values,
            OdooEnvironment env,
            ModelHandle model,
            int[] recordIds)
        {
            // Batch trigger - can be optimized to aggregate
            foreach (var recordId in recordIds)
            {
                TriggerModified(values, env, model, recordId);
            }
        }
    }
}
```

### 2.3 Updated Pipelines (Typed Create/Write with Batch Support)

**Generated:** `{ClassName}Pipelines.g.cs` (updated)

```csharp
// <auto-generated/>
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Odoo.Core;

namespace Odoo.Generated.OdooBase
{
    public static partial class ResPartnerPipelines
    {
        #region Typed Create Pipeline (Primary Fast Path)
        
        /// <summary>
        /// Create single record using typed values - the fast path.
        /// No dictionary conversion, no reflection.
        /// </summary>
        public static ResPartner Create(IEnvironment env, ResPartnerValues values)
        {
            // Get typed pipeline
            var pipeline = env.GetPipeline<Func<IEnvironment, ResPartnerValues, ResPartner>>(
                "res.partner", "create_typed");
            return pipeline(env, values);
        }
        
        /// <summary>
        /// Batch create multiple records using typed values - returns RecordSet.
        /// Odoo pattern: records = Model.create([{...}, {...}, {...}])
        /// </summary>
        public static RecordSet<IPartnerBase> Create(
            IEnvironment env,
            IEnumerable<ResPartnerValues> valuesCollection)
        {
            // Get batch typed pipeline
            var pipeline = env.GetPipeline<Func<IEnvironment, IEnumerable<ResPartnerValues>, RecordSet<IPartnerBase>>>(
                "res.partner", "create_typed_batch");
            return pipeline(env, valuesCollection);
        }
        
        /// <summary>
        /// Base implementation for single typed create.
        /// </summary>
        public static ResPartner Create_Typed_Base(IEnvironment env, ResPartnerValues values)
        {
            // 1. Allocate ID
            var newId = env.IdGenerator.NextId("res.partner");
            var modelToken = ModelSchema.ResPartner.ModelToken;
            var handle = new RecordHandle(env, newId, modelToken);
            var record = new ResPartner(handle);
            
            // 2. Register in identity map
            if (env is OdooEnvironment odooEnv)
            {
                odooEnv.RegisterInIdentityMap(modelToken.Token, newId, record);
            }
            
            // 3. Apply values to cache using typed handler (NO REFLECTION)
            ResPartnerValuesHandler.Instance.ApplyToCache(values, env.Columns, modelToken, newId);
            
            // 4. Trigger computed field dependencies
            if (env is OdooEnvironment odooEnv2)
            {
                ResPartnerValuesHandler.Instance.TriggerModified(values, odooEnv2, modelToken, newId);
            }
            
            return record;
        }
        
        /// <summary>
        /// Base implementation for batch typed create.
        /// Creates all records, returns RecordSet with all IDs.
        /// </summary>
        public static RecordSet<IPartnerBase> Create_Typed_Batch_Base(
            IEnvironment env,
            IEnumerable<ResPartnerValues> valuesCollection)
        {
            var modelToken = ModelSchema.ResPartner.ModelToken;
            var valuesList = valuesCollection.ToList();
            var recordIds = new int[valuesList.Count];
            
            // 1. Allocate all IDs upfront
            for (int i = 0; i < valuesList.Count; i++)
            {
                recordIds[i] = env.IdGenerator.NextId("res.partner");
            }
            
            // 2. Create records and register in identity map
            if (env is OdooEnvironment odooEnv)
            {
                for (int i = 0; i < valuesList.Count; i++)
                {
                    var handle = new RecordHandle(env, recordIds[i], modelToken);
                    var record = new ResPartner(handle);
                    odooEnv.RegisterInIdentityMap(modelToken.Token, recordIds[i], record);
                }
            }
            
            // 3. Batch apply values to cache
            ResPartnerValuesHandler.Instance.ApplyToCacheBatch(
                valuesList, env.Columns, modelToken, recordIds);
            
            // 4. Trigger computed field dependencies for each
            if (env is OdooEnvironment odooEnv2)
            {
                for (int i = 0; i < valuesList.Count; i++)
                {
                    ResPartnerValuesHandler.Instance.TriggerModified(
                        valuesList[i], odooEnv2, modelToken, recordIds[i]);
                }
            }
            
            // 5. Return RecordSet (Odoo pattern)
            return env.CreateRecordSet<IPartnerBase>(recordIds);
        }
        
        #endregion
        
        #region Dictionary Create (Python Interop Path)
        
        /// <summary>
        /// Create single record using dictionary - for Python interop.
        /// Converts to typed values internally.
        /// </summary>
        public static ResPartner Create(IEnvironment env, Dictionary<string, object?> vals)
        {
            var typedValues = ResPartnerValues.FromDictionary(vals);
            return Create(env, typedValues);
        }
        
        /// <summary>
        /// Batch create records using dictionaries - for Python interop.
        /// Converts to typed values internally.
        /// </summary>
        public static RecordSet<IPartnerBase> Create(
            IEnvironment env,
            IEnumerable<Dictionary<string, object?>> valsDicts)
        {
            var typedValues = ResPartnerValues.FromDictionaries(valsDicts);
            return Create(env, typedValues);
        }
        
        #endregion
        
        #region Typed Write Pipeline
        
        /// <summary>
        /// Write to single record using typed values - fast path.
        /// </summary>
        public static void Write(RecordHandle handle, ResPartnerValues values)
        {
            var pipeline = handle.Env.GetPipeline<Action<RecordHandle, ResPartnerValues>>(
                "res.partner", "write_typed");
            pipeline(handle, values);
        }
        
        /// <summary>
        /// Write to multiple records using typed values - Odoo pattern.
        /// Applies same values to all records in the set.
        /// Odoo pattern: records.write(vals)
        /// </summary>
        public static void Write(RecordSet<IPartnerBase> records, ResPartnerValues values)
        {
            var pipeline = records.Env.GetPipeline<Action<RecordSet<IPartnerBase>, ResPartnerValues>>(
                "res.partner", "write_typed_batch");
            pipeline(records, values);
        }
        
        /// <summary>
        /// Base implementation for single typed write.
        /// </summary>
        public static void Write_Typed_Base(RecordHandle handle, ResPartnerValues values)
        {
            var modelToken = ModelSchema.ResPartner.ModelToken;
            
            // Apply values to cache (NO REFLECTION)
            ResPartnerValuesHandler.Instance.ApplyToCache(values, handle.Env.Columns, modelToken, handle.Id);
            
            // Mark dirty
            MarkDirtyFields(handle.Env.Columns, modelToken, handle.Id, values);
            
            // Trigger modified
            if (handle.Env is OdooEnvironment odooEnv)
            {
                ResPartnerValuesHandler.Instance.TriggerModified(values, odooEnv, modelToken, handle.Id);
            }
        }
        
        /// <summary>
        /// Base implementation for batch typed write.
        /// Applies same values to all records in the set.
        /// </summary>
        public static void Write_Typed_Batch_Base(RecordSet<IPartnerBase> records, ResPartnerValues values)
        {
            var modelToken = ModelSchema.ResPartner.ModelToken;
            
            // Bulk apply values to all records
            ResPartnerValuesHandler.Instance.ApplyToCacheBulk(
                values, records.Env.Columns, modelToken, records.Ids);
            
            // Mark dirty for all records
            foreach (var id in records.Ids)
            {
                MarkDirtyFields(records.Env.Columns, modelToken, id, values);
            }
            
            // Trigger modified for all records
            if (records.Env is OdooEnvironment odooEnv)
            {
                ResPartnerValuesHandler.Instance.TriggerModifiedBatch(values, odooEnv, modelToken, records.Ids);
            }
        }
        
        private static void MarkDirtyFields(
            IColumnarCache cache,
            ModelHandle modelToken,
            int recordId,
            ResPartnerValues values)
        {
            var setFields = values.GetSetFields();
            foreach (var fieldName in setFields)
            {
                var fieldToken = GetFieldToken(fieldName);
                if (fieldToken.HasValue)
                {
                    cache.MarkDirty(modelToken, recordId, fieldToken.Value);
                }
            }
        }
        
        private static FieldHandle? GetFieldToken(string fieldName)
        {
            return fieldName switch
            {
                "name" => ModelSchema.ResPartner.Name,
                "is_company" => ModelSchema.ResPartner.IsCompany,
                "display_name" => ModelSchema.ResPartner.DisplayName,
                _ => null
            };
        }
        
        #endregion
    }
}
```

### 2.4 Environment Extensions (Updated with Batch Support)

**Generated:** `OdooEnvironmentExtensions.g.cs` (updated)

```csharp
// <auto-generated/>
#nullable enable

using System;
using System.Collections.Generic;
using Odoo.Core;

namespace Odoo.Generated.OdooBase
{
    public static partial class OdooEnvironmentExtensions
    {
        #region Single Record Create
        
        /// <summary>
        /// Create a single res.partner using typed values (fast path).
        /// </summary>
        /// <example>
        /// var partner = env.Create(new ResPartnerValues { Name = "Alice" });
        /// </example>
        public static IPartnerBase Create(this IEnvironment env, ResPartnerValues values)
        {
            return ResPartnerPipelines.Create(env, values);
        }
        
        /// <summary>
        /// Create a single res.partner using dictionary (Python interop).
        /// </summary>
        public static ResPartner CreateResPartner(this IEnvironment env, Dictionary<string, object?> vals)
        {
            return ResPartnerPipelines.Create(env, vals);
        }
        
        #endregion
        
        #region Batch Record Create
        
        /// <summary>
        /// Create multiple res.partner records using typed values (fast path).
        /// Named CreateBatch to avoid ambiguity with single Create
        /// (allows target-typed new() syntax for single record creation).
        /// Returns RecordSet (Odoo pattern).
        /// </summary>
        /// <example>
        /// var partners = env.CreateBatch(new[] {
        ///     new ResPartnerValues { Name = "Alice" },
        ///     new ResPartnerValues { Name = "Bob" },
        ///     new ResPartnerValues { Name = "Charlie" }
        /// });
        /// // partners.Count == 3
        /// </example>
        public static RecordSet<IPartnerBase> CreateBatch(
            this IEnvironment env,
            IEnumerable<ResPartnerValues> valuesCollection)
        {
            return ResPartnerPipelines.Create(env, valuesCollection);
        }
        
        /// <summary>
        /// Create multiple res.partner records using dictionaries (Python interop).
        /// Returns RecordSet (Odoo pattern).
        /// </summary>
        public static RecordSet<IPartnerBase> CreateResPartners(
            this IEnvironment env,
            IEnumerable<Dictionary<string, object?>> valsDicts)
        {
            return ResPartnerPipelines.Create(env, valsDicts);
        }
        
        #endregion
        
        #region Batch Write Extensions
        
        /// <summary>
        /// Write same values to all records in the set.
        /// Odoo pattern: records.write(vals)
        /// </summary>
        /// <example>
        /// var partners = env.GetRecords<IPartnerBase>(new[] { 1, 2, 3 });
        /// partners.Write(new ResPartnerValues { IsCompany = true });
        /// </example>
        public static void Write(
            this RecordSet<IPartnerBase> records,
            ResPartnerValues values)
        {
            ResPartnerPipelines.Write(records, values);
        }
        
        #endregion
    }
}
```

---

## Part 3: Addon Values Composition

### 3.1 Interface-Based Composition

When addons extend models, they add new fields. The values classes support this through interfaces:

**In `Odoo.Base`:**
```csharp
// Generated interface for base fields
public interface IPartnerBaseValues
{
    RecordValueField<string> Name { get; set; }
    RecordValueField<bool> IsCompany { get; set; }
}
```

**In `Odoo.Sale`:**
```csharp
// Generated interface for sale extension fields
public interface IPartnerSaleValues
{
    RecordValueField<decimal> CreditLimit { get; set; }
}

// Unified values class implementing both
public sealed class ResPartnerValues : IPartnerBaseValues, IPartnerSaleValues, IRecordValues<IPartnerBase>
{
    // Base fields
    public RecordValueField<string> Name { get; set; } = new();
    public RecordValueField<bool> IsCompany { get; set; } = new();
    
    // Sale extension fields
    public RecordValueField<decimal> CreditLimit { get; set; } = new();
    
    // ... implementation
}
```

### 3.2 Addon Hook Example (Single Create)

Addons can intercept and modify values during creation:

```csharp
// In Sale addon
[OdooLogic("res.partner", "create_typed")]
public static ResPartner CreateWithDefaults(
    IEnvironment env,
    ResPartnerValues values,
    Func<IEnvironment, ResPartnerValues, ResPartner> super)
{
    // Addon logic: Set default credit limit for companies
    if (values.IsCompany.IsSet && values.IsCompany.Value)
    {
        if (!values.CreditLimit.IsSet)
        {
            values.CreditLimit = 10000m;  // In-place mutation!
        }
    }
    
    // Continue to base implementation
    return super(env, values);
}
```

### 3.3 Addon Hook Example (Batch Create)

Batch create hooks receive the collection and can modify each values instance:

```csharp
// In Sale addon - batch create hook
[OdooLogic("res.partner", "create_typed_batch")]
public static RecordSet<IPartnerBase> CreateBatchWithDefaults(
    IEnvironment env,
    IEnumerable<ResPartnerValues> valuesCollection,
    Func<IEnvironment, IEnumerable<ResPartnerValues>, RecordSet<IPartnerBase>> super)
{
    // Apply defaults to each values in batch
    foreach (var values in valuesCollection)
    {
        if (values.IsCompany.IsSet && values.IsCompany.Value)
        {
            if (!values.CreditLimit.IsSet)
            {
                values.CreditLimit = 10000m;  // In-place mutation!
            }
        }
    }
    
    // Continue to base implementation
    return super(env, valuesCollection);
}
```

### 3.4 Addon Hook Example (Batch Write)

Batch write hooks receive the RecordSet and values:

```csharp
// In Sale addon - batch write hook
[OdooLogic("res.partner", "write_typed_batch")]
public static void WriteBatchWithValidation(
    RecordSet<IPartnerBase> records,
    ResPartnerValues values,
    Action<RecordSet<IPartnerBase>, ResPartnerValues> super)
{
    // Validation before write
    if (values.CreditLimit.IsSet && values.CreditLimit.Value < 0)
    {
        throw new InvalidOperationException("Credit limit cannot be negative");
    }
    
    // Continue to base implementation
    super(records, values);
}
```

---

## Part 4: Implementation Plan

### Phase 1: Core Infrastructure
1. Add `RecordValueField<T>` class to `Odoo.Core`
2. Add `IRecordValues` and `IRecordValues<TRecord>` interfaces
3. Add `IRecordValuesHandler<T>` interface with batch methods

### Phase 2: Generator Updates
1. Update generator to produce `{Model}Values` classes with `RecordValueField<T>`
2. Generate `{Model}ValuesHandler` with batch methods
3. Update `{Model}Pipelines` with typed Create/Write methods including batch overloads
4. Update environment extensions with batch methods

### Phase 3: Pipeline Registration
1. Register `create_typed` and `create_typed_batch` pipelines
2. Register `write_typed` and `write_typed_batch` pipelines
3. Ensure dictionary methods convert to typed and call typed pipelines

### Phase 4: Testing
1. Update existing tests to use new typed API
2. Add tests for `RecordValueField<T>.IsSet` tracking
3. Add tests for batch create (returns RecordSet)
4. Add tests for batch write (same values to multiple records)
5. Add tests for addon mutation scenarios
6. Add performance benchmarks comparing typed vs dictionary paths

---

## Performance Comparison

| Operation | Dictionary Path | Typed Path |
|-----------|----------------|------------|
| Field value access | Dictionary lookup + unboxing | Direct field access |
| IsSet check | Dictionary.ContainsKey() | Boolean field read |
| Apply to cache | Reflection/switch on strings | Generated direct calls |
| Type safety | Runtime | Compile-time |
| Batch create | N iterations of dict conversion | Direct batch apply |
| Batch write | N iterations | Single bulk apply call |

Expected improvement: **3-5x faster** for single operations, **5-10x faster** for batch operations.

---

## API Summary

### Single Record Operations
```csharp
// Create single record (fast path)
var partner = env.Create(new ResPartnerValues { Name = "Alice" });

// Write to single record
partner.Handle.Write(new ResPartnerValues { IsCompany = true });
```

### Batch Record Operations
```csharp
// Batch create - uses CreateBatch to avoid ambiguity with single Create
// (allows target-typed new() syntax for single record creation)
var partners = env.CreateBatch(new[] {
    new ResPartnerValues { Name = "Alice" },
    new ResPartnerValues { Name = "Bob" },
    new ResPartnerValues { Name = "Charlie" }
});
// partners.Count == 3

// Batch write - same values to all records (Odoo pattern)
partners.Write(new ResPartnerValues { IsCompany = true });
```

### Python Interop (Dictionary Path)
```csharp
// Single create via dictionary (converts internally)
var partner = ResPartnerPipelines.Create(env, new() { { "name", "Alice" } });

// Batch create via dictionaries
var partners = env.CreateResPartners(new[] {
    new Dictionary<string, object?> { { "name", "Alice" } },
    new Dictionary<string, object?> { { "name", "Bob" } }
});
```

---

## Migration Path

### Backward Compatibility
- Dictionary-based `Create(env, dict)` still works
- Internally converts to typed values
- Tests using dictionary syntax continue to pass

### New Recommended API
```csharp
// OLD (still works via conversion)
var partner = ResPartnerPipelines.Create(env, new() { { "name", "Alice" } });

// NEW (fast path - single)
var partner = env.Create(new ResPartnerValues { Name = "Alice" });

// NEW (fast path - batch)
var partners = env.CreateBatch(new[] {
    new ResPartnerValues { Name = "Alice" },
    new ResPartnerValues { Name = "Bob" }
});
```

---

## File Changes Summary

### New Files in `Odoo.Core`
- `RecordValueField.cs` - RecordValueField<T> wrapper class
- `IRecordValues.cs` - Base interfaces for values (`IRecordValues`, `IRecordValues<T>`)
- `IRecordValuesHandler.cs` - Handler interface with batch methods

### Modified Files in `Odoo.SourceGenerator`
- `OdooModelGenerator.cs`:
  - Update `GenerateValuesStruct` → `GenerateValuesClass` with `RecordValueField<T>`
  - Add `GenerateValuesHandler` with batch methods
  - Update `GeneratePropertyPipelines` for typed Create/Write with batch overloads
  - Update `GenerateEnvironmentExtensions` with batch methods
  - Update `GenerateModuleRegistrar` for all typed pipeline registrations

---

## Error Handling

All operations throw on error, relying on database ACID guarantees:

```csharp
// Batch create - if any record fails, entire batch fails
try
{
    var partners = env.Create(new[] {
        new ResPartnerValues { Name = "Alice" },
        new ResPartnerValues { Name = null! }, // Invalid!
        new ResPartnerValues { Name = "Charlie" }
    });
}
catch (Exception ex)
{
    // No records created - transaction rolled back
}
```
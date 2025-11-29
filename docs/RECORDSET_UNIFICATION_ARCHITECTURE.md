# RecordSet/Record Unification Architecture

## Status: IMPLEMENTED ✅

This document describes the Odoo-aligned pattern where records and recordsets share the same interface. In Odoo, a single record is just a recordset with one element.

**Implementation completed:** All 89 tests passing.

## Key Insight from Odoo

In Odoo Python:
```python
partner = env['res.partner'].browse(1)      # recordset with 1 element
partners = env['res.partner'].browse([1, 2, 3])  # recordset with 3 elements

# Both have the SAME interface:
partner.name        # works
partners.name       # raises for multi-record (singleton-only)
partner.write({'name': 'X'})   # works - writes to 1
partners.write({'name': 'X'})  # works - writes to all
```

## Architecture (Implemented)

### Generated Wrapper Pattern

```csharp
public sealed class ResPartner : IPartnerBase, IModel, IRecordWrapper
{
    internal readonly IEnvironment _env;
    internal readonly ModelHandle _model;
    internal readonly RecordId[] _ids;  // Can be 0, 1, or many records
    
    // Primary constructor - multi-record capable
    public ResPartner(IEnvironment env, ModelHandle model, RecordId[] ids)
    {
        _env = env;
        _model = model;
        _ids = ids;
    }
    
    // Backward-compatible constructor from RecordHandle
    public ResPartner(RecordHandle handle)
        : this(handle.Env, handle.Model, new[] { handle.Id }) { }
    
    // Handle property - singleton only (throws for multi-record)
    public RecordHandle Handle => _ids.Length == 1
        ? new RecordHandle(_env, _ids[0], _model)
        : throw new InvalidOperationException($"Expected singleton {ModelName}(...), got {_ids.Length} records");
    
    // Id - singleton only (Odoo pattern)
    public RecordId Id => _ids.Length == 1
        ? _ids[0]
        : throw new InvalidOperationException($"Expected singleton {ModelName}(...), got {_ids.Length} records");
    
    // Ids - always available (Odoo pattern)
    public RecordId[] Ids => _ids;
    
    public IEnvironment Env => _env;
    public int Count => _ids.Length;
    public bool IsSingleton => _ids.Length == 1;
    
    // EnsureOne - used by property setters
    private void EnsureOne()
    {
        if (_ids.Length != 1)
            throw new InvalidOperationException($"Expected singleton {ModelName}(...), got {_ids.Length} records");
    }
    
    // Write - applies to ALL records (Odoo pattern)
    public bool Write(IRecordValues vals)
    {
        var pipeline = Env.GetPipeline<Action<RecordHandle, IRecordValues>>("res.partner", "write");
        // Write to all records in the recordset
        foreach (var id in _ids)
        {
            var handle = new RecordHandle(_env, id, _model);
            pipeline(handle, vals);
        }
        return true;
    }
    
    // Property getter - uses Id (singleton only)
    public string Name
    {
        get => Env.Columns.GetValue<string>(ModelSchema.ResPartner.ModelToken, Id, ModelSchema.ResPartner.Name);
        set
        {
            EnsureOne();  // Property setters are singleton-only
            var vals = new Dictionary<string, object?> { { "name", value } };
            ResPartnerPipelines.WriteFromDict(Handle, vals);
        }
    }
    
    // ToString shows all IDs
    public override string ToString()
    {
        return _ids.Length == 1 
            ? $"res.partner({_ids[0].Value})" 
            : $"res.partner([{string.Join(", ", _ids.Select(id => id.Value))}])";
    }
}
```

## Behavior Summary

| Operation | Singleton | Multi-Record |
|-----------|-----------|--------------|
| `record.Id` | Returns ID | Throws |
| `record.Ids` | Returns `[id]` | Returns `[id1, id2, ...]` |
| `record.Count` | 1 | N |
| `record.IsSingleton` | true | false |
| `record.Name` (get) | Returns value | Throws (uses `Id`) |
| `record.Name = "X"` (set) | Works | Throws (`EnsureOne`) |
| `record.Write(vals)` | Writes to 1 | Writes to all |
| `record.Handle` | Returns handle | Throws |
| `record.ToString()` | `"res.partner(1)"` | `"res.partner([1, 2, 3])"` |

## Usage Examples

```csharp
// Singleton usage (existing pattern works)
var partner = env.Create(new ResPartnerValues { Name = "Alice" });
partner.Name = "Bob";  // Works - singleton
partner.Write(new ResPartnerValues { IsCustomer = true });  // Works

// Multi-record usage (new capability)
var multiRecord = new ResPartner(env, ModelSchema.ResPartner.ModelToken, new[] { id1, id2, id3 });

// These work:
multiRecord.Write(new ResPartnerValues { IsActive = true });  // Writes to all 3
var count = multiRecord.Count;  // 3
var ids = multiRecord.Ids;  // [id1, id2, id3]

// These throw (singleton-only):
// var id = multiRecord.Id;  // Throws
// multiRecord.Name = "X";   // Throws
// var name = multiRecord.Name;  // Throws (property getter uses Id)
```

## Source Generator Changes

The [`OdooModelGenerator.GenerateWrapperStruct()`](../src/Odoo.SourceGenerator/OdooModelGenerator.cs:416) method was updated to:

1. **Changed storage**: `RecordHandle _handle` → `RecordId[] _ids` + `_env` + `_model`
2. **Added `Ids` property**: Always available (Odoo pattern)
3. **Updated `Id` property**: Throws for non-singleton
4. **Added `Count`, `IsSingleton`**: Convenience properties
5. **Added `EnsureOne()`**: Private helper for property setters
6. **Updated `Write()`**: Loops over all `_ids`
7. **Updated property setters**: Call `EnsureOne()` before writing
8. **Backward compatibility**: `RecordHandle` constructor delegates to array constructor

## Test Coverage

14 new tests added in `DiamondInheritanceTests.RecordSet/Record Unification Tests`:

- `RecordSetUnification_Ids_AlwaysAvailable` - Ids works for singleton
- `RecordSetUnification_Count_ReturnsRecordCount` - Count is correct
- `RecordSetUnification_IsSingleton_TrueForSingleRecord` - IsSingleton works
- `RecordSetUnification_MultiRecordWrapper_IdsWorks` - Ids works for multi-record
- `RecordSetUnification_MultiRecordWrapper_CountIs3` - Count for 3 records
- `RecordSetUnification_MultiRecord_IdThrows` - Id throws for multi-record
- `RecordSetUnification_MultiRecord_HandleThrows` - Handle throws for multi-record
- `RecordSetUnification_MultiRecord_WriteAffectsAllRecords` - Write updates all
- `RecordSetUnification_MultiRecord_PropertySetterThrows` - Setter throws for multi
- `RecordSetUnification_SingleRecord_PropertyGetterWorks` - Getter works for singleton
- `RecordSetUnification_ToString_ShowsAllIds` - ToString formats correctly
- `RecordSetUnification_EmptyRecordset_WorksCorrectly` - Empty recordset handling
- `RecordSetUnification_Equality_SameIds` - Equal for same IDs
- `RecordSetUnification_Equality_DifferentIds` - Not equal for different IDs

## Next Steps

### Dynamic Method Discovery

The next phase will update the source generator to discover OdooLogic methods and generate wrapper methods from them, rather than hardcoding Write/Create.

When generating wrapper methods from discovered OdooLogic methods:
- Use `IMethodSymbol.Name` directly (already PascalCase in C#)
- The `MethodName` from the attribute (`[OdooLogic("res.partner", "action_verify")]`) is for pipeline registration
- The C# method name comes from the symbol

```csharp
// Discovery finds:
[OdooLogic("res.partner", "action_verify")]
public static void ActionVerify(RecordSet<IPartnerBase> self) { }

// Generator uses:
// - method.MethodSymbol.Name = "ActionVerify"  (for generated wrapper method)
// - method.MethodName = "action_verify"        (for pipeline key)
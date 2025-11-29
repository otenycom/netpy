using System;
using System.Collections.Generic;

namespace Odoo.Core.Modules
{
    /// <summary>
    /// Describes a field's metadata including compute, dependencies, and inverse information.
    /// Mirrors Odoo's field descriptors with support for computed fields.
    /// </summary>
    public class FieldSchema
    {
        // --- Basic Properties ---
        
        public string FieldName { get; }
        public Type FieldType { get; }
        public bool IsReadOnly { get; }
        public Type ContributingInterface { get; }
        public FieldHandle Token { get; }

        // --- Computed Field Properties ---
        
        /// <summary>
        /// If true, this field is computed (has a compute method).
        /// </summary>
        public bool IsComputed { get; set; }
        
        /// <summary>
        /// If true, this computed field is stored in the database.
        /// Stored computed fields are updated when dependencies change.
        /// </summary>
        public bool IsStored { get; set; }
        
        /// <summary>
        /// The name of the compute method (e.g., "_compute_display_name").
        /// </summary>
        public string? ComputeMethodName { get; set; }
        
        /// <summary>
        /// The delegate type for the compute method.
        /// </summary>
        public Type? ComputeMethodType { get; set; }
        
        /// <summary>
        /// Fields that this computed field depends on.
        /// When these fields change, this field needs recomputation.
        /// Format: ["field_name"] for same model, ["related_field.field_name"] for related.
        /// </summary>
        public List<string> Dependencies { get; } = new();
        
        /// <summary>
        /// The name of the inverse method for setting a computed field.
        /// Allows writing to computed fields by delegating to the inverse method.
        /// </summary>
        public string? InverseMethodName { get; set; }
        
        /// <summary>
        /// Related field path for related fields (e.g., "partner_id.name").
        /// </summary>
        public string? Related { get; set; }
        
        // --- Default Value ---
        
        /// <summary>
        /// Default value factory method name for creating new records.
        /// </summary>
        public string? DefaultMethodName { get; set; }
        
        /// <summary>
        /// Static default value (if not using a factory method).
        /// </summary>
        public object? DefaultValue { get; set; }
        
        // --- Constraints ---
        
        /// <summary>
        /// If true, this field is required (NOT NULL).
        /// </summary>
        public bool Required { get; set; }
        
        /// <summary>
        /// If true, changes to this field are tracked in the chatter.
        /// </summary>
        public bool Tracking { get; set; }

        public FieldSchema(string fieldName, Type fieldType, bool isReadOnly, Type contributingInterface, FieldHandle token)
        {
            FieldName = fieldName;
            FieldType = fieldType;
            IsReadOnly = isReadOnly;
            ContributingInterface = contributingInterface;
            Token = token;
        }
        
        /// <summary>
        /// Returns true if this field can be written to.
        /// Computed fields without inverse cannot be written.
        /// </summary>
        public bool IsWritable => !IsReadOnly && (!IsComputed || InverseMethodName != null);
        
        /// <summary>
        /// Returns true if this field triggers recomputation of dependent fields when changed.
        /// </summary>
        public bool HasDependents { get; set; }
    }
}
using System;

namespace Odoo.Core
{
    /// <summary>
    /// Marks a property as computed by a specific method.
    /// Mirrors Odoo's compute="method_name" field parameter.
    /// </summary>
    /// <example>
    /// <code>
    /// [OdooCompute(nameof(ComputeDisplayName))]
    /// string DisplayName { get; }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class OdooComputeAttribute : Attribute
    {
        /// <summary>
        /// The name of the compute method (e.g., "_compute_display_name").
        /// </summary>
        public string MethodName { get; }
        
        /// <summary>
        /// If true, the computed value is stored in the database.
        /// Stored computed fields are updated when dependencies change.
        /// Default: false (computed on-the-fly).
        /// </summary>
        public bool Stored { get; set; }
        
        /// <summary>
        /// If true, the field is always recomputed regardless of cache.
        /// Default: false.
        /// </summary>
        public bool Readonly { get; set; } = true;

        public OdooComputeAttribute(string methodName)
        {
            MethodName = methodName;
        }
    }

    /// <summary>
    /// Declares field dependencies for computed fields.
    /// When any dependency changes, the computed field is recomputed.
    /// Mirrors Odoo's @api.depends decorator.
    /// </summary>
    /// <example>
    /// <code>
    /// [OdooDepends("FirstName", "LastName")]
    /// [OdooCompute(nameof(ComputeFullName))]
    /// string FullName { get; }
    /// 
    /// // For related fields:
    /// [OdooDepends("PartnerId.Name")]
    /// [OdooCompute(nameof(ComputePartnerName))]
    /// string PartnerName { get; }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class OdooDependsAttribute : Attribute
    {
        /// <summary>
        /// Field names that trigger recomputation.
        /// Use dot notation for related fields (e.g., "partner_id.name").
        /// </summary>
        public string[] Fields { get; }

        public OdooDependsAttribute(params string[] fields)
        {
            Fields = fields ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Specifies an inverse method for a computed field, allowing writes.
    /// Mirrors Odoo's inverse="method_name" field parameter.
    /// </summary>
    /// <example>
    /// <code>
    /// [OdooCompute(nameof(ComputeFullName))]
    /// [OdooInverse(nameof(InverseFullName))]
    /// [OdooDepends("FirstName", "LastName")]
    /// string FullName { get; set; }
    /// 
    /// void InverseFullName(RecordHandle handle, string value)
    /// {
    ///     var parts = value.Split(' ');
    ///     handle.As&lt;IPartner&gt;().FirstName = parts[0];
    ///     handle.As&lt;IPartner&gt;().LastName = parts.Length > 1 ? parts[1] : "";
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class OdooInverseAttribute : Attribute
    {
        /// <summary>
        /// The name of the inverse method.
        /// </summary>
        public string MethodName { get; }

        public OdooInverseAttribute(string methodName)
        {
            MethodName = methodName;
        }
    }

    /// <summary>
    /// Specifies a default value or default value factory for a field.
    /// Mirrors Odoo's default=value or default=lambda self: value field parameter.
    /// </summary>
    /// <example>
    /// <code>
    /// // Static default:
    /// [OdooDefault("New")]
    /// string Status { get; set; }
    /// 
    /// // Default from method:
    /// [OdooDefault(nameof(DefaultDate))]
    /// DateTime CreateDate { get; set; }
    /// 
    /// DateTime DefaultDate(IEnvironment env) => DateTime.UtcNow;
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class OdooDefaultAttribute : Attribute
    {
        /// <summary>
        /// Static default value (for simple types).
        /// </summary>
        public object? Value { get; }
        
        /// <summary>
        /// Name of a method that returns the default value.
        /// Signature: T DefaultMethod(IEnvironment env)
        /// </summary>
        public string? MethodName { get; }
        
        /// <summary>
        /// If true, the Value property contains a method name, not a static value.
        /// </summary>
        public bool IsMethodReference { get; }

        /// <summary>
        /// Create a default attribute with a static value.
        /// </summary>
        public OdooDefaultAttribute(object value)
        {
            Value = value;
            IsMethodReference = false;
        }
        
        /// <summary>
        /// Create a default attribute with a method reference.
        /// </summary>
        /// <param name="methodName">Name of the default value factory method</param>
        /// <param name="isMethod">Must be true to indicate this is a method reference</param>
        public OdooDefaultAttribute(string methodName, bool isMethod)
        {
            if (isMethod)
            {
                MethodName = methodName;
                IsMethodReference = true;
            }
            else
            {
                Value = methodName;
                IsMethodReference = false;
            }
        }
    }

    /// <summary>
    /// Marks a property as a related field (shortcut to a related record's field).
    /// Mirrors Odoo's related="field.field" field parameter.
    /// </summary>
    /// <example>
    /// <code>
    /// [OdooRelated("partner_id.name")]
    /// string PartnerName { get; set; }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class OdooRelatedAttribute : Attribute
    {
        /// <summary>
        /// The related field path using dot notation.
        /// </summary>
        public string FieldPath { get; }
        
        /// <summary>
        /// If true, the related value is stored in the database.
        /// </summary>
        public bool Stored { get; set; }

        public OdooRelatedAttribute(string fieldPath)
        {
            FieldPath = fieldPath;
        }
    }

    /// <summary>
    /// Marks a field as required (NOT NULL constraint).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class OdooRequiredAttribute : Attribute
    {
        /// <summary>
        /// Condition for when the field is required.
        /// If null, field is always required.
        /// </summary>
        public string? Condition { get; }

        public OdooRequiredAttribute(string? condition = null)
        {
            Condition = condition;
        }
    }

    /// <summary>
    /// Marks a field as tracking changes in the record's history/chatter.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class OdooTrackingAttribute : Attribute
    {
        /// <summary>
        /// Tracking priority (higher = more important).
        /// </summary>
        public int Sequence { get; }

        public OdooTrackingAttribute(int sequence = 100)
        {
            Sequence = sequence;
        }
    }

    /// <summary>
    /// Specifies that a method is a constraint that validates record data.
    /// Mirrors Odoo's @api.constrains decorator.
    /// </summary>
    /// <example>
    /// <code>
    /// [OdooConstrains("email")]
    /// void CheckEmail(RecordSet&lt;IPartner&gt; self)
    /// {
    ///     foreach (var partner in self)
    ///     {
    ///         if (!partner.Email.Contains("@"))
    ///             throw new ValidationException("Invalid email");
    ///     }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class OdooConstrainsAttribute : Attribute
    {
        /// <summary>
        /// Field names that trigger the constraint check.
        /// </summary>
        public string[] Fields { get; }

        public OdooConstrainsAttribute(params string[] fields)
        {
            Fields = fields ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Specifies that a method should be called when certain fields change.
    /// Mirrors Odoo's @api.onchange decorator.
    /// </summary>
    /// <example>
    /// <code>
    /// [OdooOnchange("product_id")]
    /// void OnchangeProduct(RecordSet&lt;ISaleOrderLine&gt; self)
    /// {
    ///     foreach (var line in self)
    ///     {
    ///         line.Price = line.ProductId?.ListPrice ?? 0;
    ///     }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class OdooOnchangeAttribute : Attribute
    {
        /// <summary>
        /// Field names that trigger the onchange.
        /// </summary>
        public string[] Fields { get; }

        public OdooOnchangeAttribute(params string[] fields)
        {
            Fields = fields ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Marks a method as an override of a base model method in the pipeline.
    /// The method will be added to the pipeline and can call super() to invoke prior handlers.
    /// Mirrors Odoo's method overriding with super() pattern.
    /// </summary>
    /// <example>
    /// <code>
    /// [OdooOverride("res.partner", "write")]
    /// void WriteWithAudit(RecordHandle[] self, Dictionary&lt;string, object?&gt; vals, Action&lt;RecordHandle[], Dictionary&lt;string, object?&gt;&gt; super)
    /// {
    ///     // Custom logic before
    ///     Console.WriteLine($"Writing to {self.Length} records");
    ///     
    ///     // Call the base implementation
    ///     super(self, vals);
    ///     
    ///     // Custom logic after
    ///     Console.WriteLine("Write completed");
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class OdooOverrideAttribute : Attribute
    {
        /// <summary>
        /// The model name this override applies to.
        /// </summary>
        public string ModelName { get; }
        
        /// <summary>
        /// The method name being overridden (e.g., "write", "create", "unlink").
        /// </summary>
        public string MethodName { get; }
        
        /// <summary>
        /// Priority for ordering multiple overrides. Lower values run first.
        /// Default: 10.
        /// </summary>
        public int Priority { get; set; } = 10;

        public OdooOverrideAttribute(string modelName, string methodName)
        {
            ModelName = modelName;
            MethodName = methodName;
        }
    }
}
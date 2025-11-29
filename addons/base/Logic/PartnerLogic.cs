using System;
using System.Collections.Generic;
using Odoo.Core;
using Odoo.Core.Pipeline;
using Odoo.Base.Models;

// Import the generated ModelSchema for field tokens
// Note: This will be available after source generator runs
#if ODOO_GENERATED
using Odoo.Generated.OdooBase;
#endif

namespace Odoo.Base.Logic
{
    /// <summary>
    /// Base module business logic for res.partner.
    /// This implements the BASE methods - not overrides.
    /// The generated code registers Write_Base and Create_Base automatically,
    /// so this class adds ADDITIONAL business logic via pipelines.
    /// </summary>
    public static class PartnerLogic
    {
        /// <summary>
        /// Action to verify partner data.
        /// This is a business method, not a write/create override.
        /// </summary>
        [OdooLogic("res.partner", "action_verify")]
        public static void ActionVerify(RecordSet<IPartnerBase> self)
        {
            Console.WriteLine("[Base] Verifying basic fields...");
            foreach (var partner in self)
            {
                if (string.IsNullOrEmpty(partner.Name))
                {
                    Console.WriteLine($"[Base] Error: Partner {partner.Id} has no name!");
                    // In a real app, throw exception
                }
                else
                {
                    Console.WriteLine($"[Base] Partner {partner.Name} verified.");
                }
            }
        }
        
        /// <summary>
        /// Compute display_name field for all partners in the recordset.
        ///
        /// Pattern: Batch iteration like Odoo's `for record in self:`.
        /// This method is registered via [OdooLogic] and called by the generated
        /// Compute_DisplayName method when the field needs recomputation.
        ///
        /// Logic:
        /// - Companies: "Name | Company"
        /// - Individuals: "Name"
        /// </summary>
        [OdooLogic("res.partner", "_compute_display_name")]
        public static void ComputeDisplayName(RecordSet<IPartnerBase> self)
        {
            Console.WriteLine($"[Compute] Computing display_name for {self.Count} partner(s)...");
            
            foreach (var partner in self)
            {
                var name = partner.Name ?? "";
                var displayName = partner.IsCompany
                    ? $"{name} | Company"
                    : name;
                
                // Use SetComputedValue to bypass the Write pipeline
                // This avoids infinite recursion and correctly handles computed fields
                // Note: We need to use generic field token access since ModelSchema
                // is generated per-assembly and might not be directly accessible
                SetDisplayNameValue(partner.Env, partner.Id, displayName);
                
                Console.WriteLine($"[Compute] Partner {partner.Id}: DisplayName = '{displayName}'");
            }
        }
        
        /// <summary>
        /// Helper to set computed display_name value using generic cache access.
        /// In the generated code, this would use ModelSchema.Partner.DisplayName token.
        /// </summary>
        private static void SetDisplayNameValue(IEnvironment env, int recordId, string value)
        {
            // Use stable hash code to compute the field token (same algorithm as generator)
            const string modelName = "res.partner";
            const string fieldName = "display_name";
            var modelToken = GetStableHashCode(modelName);
            var fieldToken = GetStableHashCode($"{modelName}.{fieldName}");
            
            // Use the SetComputedValue extension method
            env.SetComputedValue(
                new ModelHandle(modelToken),
                recordId,
                new FieldHandle(fieldToken),
                value);
        }
        
        /// <summary>
        /// Stable hash code algorithm - matches the source generator.
        /// </summary>
        private static int GetStableHashCode(string str)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in str)
                {
                    hash = hash * 31 + c;
                }
                return hash;
            }
        }
        
        // Note: Write_Base and Create_Base are generated automatically by the source generator.
        // Modules that want to extend write/create should use the OVERRIDE pattern below.
    }
}
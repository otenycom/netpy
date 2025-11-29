using System;
using System.Collections.Generic;
using Odoo.Base.Models;
using Odoo.Core;
using Odoo.Core.Pipeline;
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
        /// The Compute_DisplayName method wraps this call with `env.Protecting()`
        /// which allows us to use direct property assignment: `partner.DisplayName = value`.
        /// This is the exact same pattern as Odoo Python!
        ///
        /// Logic:
        /// - Companies: "Name | Company"
        /// - Individuals: "Name"
        /// </summary>
        [OdooLogic("res.partner", "_compute_display_name")]
        public static void ComputeDisplayName(RecordSet<IPartnerBase> self)
        {
            Console.WriteLine($"[Compute] Computing display_name for {self.Count} partner(s)...");

            // Simple Odoo-like pattern: for record in self: record.field = computed_value
            // The protection mechanism (env.Protecting) wraps this call, allowing direct assignment
            foreach (var partner in self)
            {
                var name = partner.Name ?? "";
                var displayName = partner.IsCompany ? $"{name} | Company" : name;

                // Direct property assignment - like Odoo Python!
                // This works because Compute_DisplayName wraps us with Protecting()
                partner.DisplayName = displayName;

                Console.WriteLine($"[Compute] Partner {partner.Id}: DisplayName = '{displayName}'");
            }
        }

        // Note: Write_Base and Create_Base are generated automatically by the source generator.
        // Modules that want to extend write/create should use the OVERRIDE pattern below.
    }
}

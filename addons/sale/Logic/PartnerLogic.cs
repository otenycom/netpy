using System;
using System.Collections.Generic;
using Odoo.Core;
using Odoo.Core.Pipeline;
using Odoo.Base.Models;
using Odoo.Sale.Models;

namespace Odoo.Sale.Logic
{
    /// <summary>
    /// Sale module extensions for res.partner.
    /// Demonstrates the Odoo-aligned override pattern for Write and Create pipelines.
    /// </summary>
    public static class PartnerLogic
    {
        /// <summary>
        /// Override for action_verify - adds credit limit checking.
        /// The 'super' parameter allows calling the next method in the pipeline chain.
        /// </summary>
        [OdooLogic("res.partner", "action_verify")]
        public static void ActionVerify(
            RecordSet<IPartnerBase> self,
            Action<RecordSet<IPartnerBase>> super)
        {
            Console.WriteLine("[Sale] PRE: Checking credit limit...");
            
            // PRE-SUPER: Validate before base logic runs
            foreach (var partner in self)
            {
                // In a real scenario, access sale-specific fields
                Console.WriteLine($"[Sale] Checking partner {partner.Id}: {partner.Name}");
            }
            
            // Call next in pipeline (eventually reaches the base implementation)
            super(self);

            Console.WriteLine("[Sale] POST: Verification complete.");
        }

        /// <summary>
        /// Override for write - adds audit logging and sale-specific validation.
        /// This demonstrates the Odoo-aligned Write override pattern.
        /// Signature: (RecordHandle, Dictionary, super) where super has same signature without 'super' param.
        /// </summary>
        [OdooLogic("res.partner", "write")]
        public static void Write_SaleOverride(
            RecordHandle handle,
            Dictionary<string, object?> vals,
            Action<RecordHandle, Dictionary<string, object?>> super)
        {
            Console.WriteLine($"[Sale] PRE-WRITE: Modifying partner {handle.Id}");
            
            // PRE-WRITE: Add audit information
            vals["write_date"] = DateTime.UtcNow;
            Console.WriteLine($"[Sale] Added write_date to values");
            
            // PRE-WRITE: Validate sale-specific business rules
            if (vals.ContainsKey("credit_limit"))
            {
                var creditLimit = Convert.ToDecimal(vals["credit_limit"]);
                if (creditLimit < 0)
                {
                    throw new InvalidOperationException("Credit limit cannot be negative!");
                }
                Console.WriteLine($"[Sale] Validated credit_limit: {creditLimit}");
            }
            
            // Call next in pipeline (Write_Base)
            super(handle, vals);
            
            // POST-WRITE: Side effects after base write completes
            Console.WriteLine($"[Sale] POST-WRITE: Partner {handle.Id} updated successfully");
        }
    }
}
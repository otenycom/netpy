using System;
using System.Collections.Generic;
using Odoo.Core;
using Odoo.Core.Pipeline;
using Odoo.Base.Models;

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
        
        // Note: Write_Base and Create_Base are generated automatically by the source generator.
        // Modules that want to extend write/create should use the OVERRIDE pattern below.
    }
}
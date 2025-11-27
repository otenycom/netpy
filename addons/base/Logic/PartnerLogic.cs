using System;
using Odoo.Core;
using Odoo.Core.Pipeline;
using Odoo.Base.Models;

namespace Odoo.Base.Logic
{
    public static class PartnerLogic
    {
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

        [OdooLogic("res.partner", "create")]
        public static RecordSet<IPartnerBase> Create(
            RecordSet<IPartnerBase> self, 
            System.Collections.Generic.Dictionary<string, object> values)
        {
            Console.WriteLine("[Base] Creating partner in database...");
            // Simulate DB insert
            int newId = new Random().Next(1000, 9999);
            Console.WriteLine($"[Base] Inserted with ID {newId}");
            
            return self.Env.CreateRecordSet<IPartnerBase>(new[] { newId });
        }
    }
}
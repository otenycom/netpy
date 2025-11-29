using System;
using System.Collections.Generic;
using Odoo.Base.Models;
using Odoo.Core;
using Odoo.Core.Pipeline;
using Odoo.Sale.Models;

namespace Odoo.Sale.Logic
{
    /// <summary>
    /// Sale module extensions for res.partner.
    /// Demonstrates the Odoo-aligned override pattern for business method pipelines.
    ///
    /// Write/Create pipeline overrides use IRecordValues interface for cross-assembly compatibility.
    /// The IRecordValues interface provides:
    /// - ToDictionary() for accessing field values
    /// - GetSetFields() for knowing which fields were set
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
            Action<RecordSet<IPartnerBase>> super
        )
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
        /// Override for write pipeline - adds sale-specific validation.
        /// Uses IRecordValues interface for cross-assembly compatibility.
        ///
        /// Because ResPartnerValues implements IRecordValues&lt;T&gt; for ALL visible interfaces,
        /// you can cast to the specific type for type-safe access:
        ///   if (vals is IRecordValues&lt;IPartnerSaleExtension&gt;) { ... }
        ///
        /// This demonstrates the Odoo pattern where modules can intercept and modify
        /// write operations to add business logic (validation, computed fields, etc.).
        /// </summary>
        [OdooLogic("res.partner", "write")]
        public static void Write_SaleOverride(
            RecordHandle handle,
            IRecordValues vals,
            Action<RecordHandle, IRecordValues> super
        )
        {
            Console.WriteLine("[Sale] Write override: validating partner data...");

            // IRecordValues provides ToDictionary() and GetSetFields() for field access
            var dict = vals.ToDictionary();
            foreach (var fieldName in vals.GetSetFields())
            {
                Console.WriteLine($"[Sale]   Field being written: {fieldName} = {dict[fieldName]}");
            }

            // Type-safe check using multi-interface implementation
            // ResPartnerValues now implements IRecordValues<T> for ALL visible interfaces,
            // so this cast will succeed for values from any downstream assembly!
            if (vals is IRecordValues<IPartnerSaleExtension> saleVals)
            {
                Console.WriteLine(
                    "[Sale]   ✓ Successfully cast to IRecordValues<IPartnerSaleExtension>"
                );
                // Now you have compile-time proof this is partner-compatible
            }

            // PRE-SUPER: Business logic goes here
            // Example: Validate credit limit, compute derived fields, etc.

            // Call next in pipeline (eventually reaches base write)
            super(handle, vals);

            Console.WriteLine("[Sale] Write override: complete.");
        }
    }
}

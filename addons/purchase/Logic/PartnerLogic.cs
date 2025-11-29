using System;
using Odoo.Base.Models;
using Odoo.Core;
using Odoo.Core.Pipeline;
using Odoo.Purchase.Models;

namespace Odoo.Purchase.Logic
{
    /// <summary>
    /// Purchase module extensions for res.partner.
    /// Demonstrates the Odoo-aligned override pattern for business method pipelines.
    /// </summary>
    public static class PartnerLogic
    {
        /// <summary>
        /// Override for _compute_display_name - appends "| Supplier" to the display name.
        ///
        /// This is the proper Odoo pattern for modifying computed fields: override the compute method.
        /// The compute method is called whenever a dependency changes (name, is_company).
        ///
        /// During compute, the DisplayName field is PROTECTED, which means the property getter
        /// will return the cached value directly without checking NeedsRecompute, preventing
        /// infinite recursion.
        /// </summary>
        [OdooLogic("res.partner", "_compute_display_name")]
        public static void ComputeDisplayName_PurchaseOverride(
            RecordSet<IPartnerBase> self,
            Action<RecordSet<IPartnerBase>> super
        )
        {
            Console.WriteLine("[Purchase] Computing display_name override...");

            // First, call the base compute to get the standard display name
            super(self);

            // Then, for each supplier, append "| Supplier" to the display name
            // The DisplayName property getter is protection-aware: since we're inside
            // the compute method and the field is protected, it returns the cached
            // value directly without triggering another recompute cycle.
            foreach (var partner in self)
            {
                // Cast to IPartnerPurchaseExtension to access IsSupplier
                var purchasePartner = (IPartnerPurchaseExtension)partner;
                if (purchasePartner.IsSupplier)
                {
                    // Safe to read DisplayName via property - it's protected during compute
                    var currentDisplayName = partner.DisplayName ?? partner.Name ?? "";

                    if (!currentDisplayName.Contains("| Supplier"))
                    {
                        var newDisplayName = $"{currentDisplayName} | Supplier";

                        // Write to computed field (we're in protected context)
                        partner.DisplayName = newDisplayName;

                        Console.WriteLine(
                            $"[Purchase] Partner {partner.Id}: DisplayName = '{newDisplayName}'"
                        );
                    }
                }
            }
        }

        /// <summary>
        /// Override for write pipeline - adds purchase-specific validation.
        ///
        /// The signature uses IPartnerPurchaseExtensionValues which extends IRecordValues&lt;IPartnerPurchaseExtension&gt;.
        /// This provides BOTH type-safe property access (vals.IsSupplier) AND pipeline compatibility.
        /// </summary>
        [OdooLogic("res.partner", "write")]
        public static void Write_PurchaseOverride(
            RecordHandle handle,
            IPartnerPurchaseExtensionValues vals,
            Action<RecordHandle, IRecordValues> super
        )
        {
            Console.WriteLine("[Purchase] Write override: processing partner data...");

            // Log which fields are being written
            foreach (var fieldName in vals.GetSetFields())
            {
                Console.WriteLine(
                    $"[Purchase]   Field being written: {fieldName} = {vals.Get<object>(fieldName)}"
                );
            }

            // Call next in pipeline (eventually reaches base write)
            super(handle, vals);

            Console.WriteLine("[Purchase] Write override: complete.");
        }
    }
}

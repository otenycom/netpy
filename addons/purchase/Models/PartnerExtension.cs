using Odoo.Base.Models;
using Odoo.Core;

namespace Odoo.Purchase.Models
{
    /// <summary>
    /// Extension to res.partner that adds purchase-specific fields.
    /// By inheriting IPartnerBase, the generated record will implement both interfaces.
    /// This is analogous to Odoo's _inherit mechanism.
    /// </summary>
    [OdooModel("res.partner")]
    public interface IPartnerPurchaseExtension : IPartnerBase
    {
        [OdooField("is_supplier")]
        bool IsSupplier { get; set; }
    }
}

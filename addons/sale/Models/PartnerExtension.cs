using Odoo.Base.Models;
using Odoo.Core;

namespace Odoo.Sale.Models
{
    /// <summary>
    /// Extension to res.partner that adds sale-specific fields.
    /// By inheriting IPartnerBase, the generated record will implement both interfaces.
    /// This is analogous to Odoo's _inherit mechanism.
    /// </summary>
    [OdooModel("res.partner")]
    public interface IPartnerSaleExtension : IPartnerBase
    {
        [OdooField("is_customer")]
        bool IsCustomer { get; set; }

        [OdooField("credit_limit")]
        decimal CreditLimit { get; set; }
    }
}

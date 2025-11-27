using Odoo.Core;

namespace Odoo.Base.Models
{
    [OdooModel("res.partner")]
    public interface IPartnerBase : IOdooRecord
    {
        [OdooField("name")]
        string Name { get; set; }
        
        [OdooField("email")]
        string? Email { get; set; }
        
        [OdooField("is_company")]
        bool IsCompany { get; set; }
    }
}
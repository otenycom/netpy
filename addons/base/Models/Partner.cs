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
        
        /// <summary>
        /// Computed display name field.
        /// Shows "Name | Company" for companies, or just "Name" for individuals.
        /// Automatically recomputed when Name or IsCompany changes.
        /// </summary>
        [OdooField("display_name")]
        [OdooCompute("_compute_display_name")]
        [OdooDepends("name", "is_company")]
        string DisplayName { get; }  // Read-only computed field
    }
}
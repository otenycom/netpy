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
        ///
        /// Note: The setter is intended for use within compute methods only.
        /// When a record is "protected" (during compute), setting this property
        /// writes directly to cache. Outside compute methods, attempting to set
        /// a computed field with no inverse will throw an error.
        /// </summary>
        [OdooField("display_name")]
        [OdooCompute("_compute_display_name")]
        [OdooDepends("name", "is_company")]
        string DisplayName { get; set; }  // Setter for compute method assignment
    }
}
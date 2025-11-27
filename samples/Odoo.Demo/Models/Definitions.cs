using Odoo.Core;

namespace Odoo.Models
{
    // --- Mixin 1: Mail Thread ---
    /// <summary>
    /// Mixin for models that support message tracking and followers.
    /// This is typically inherited by models that need social features.
    /// </summary>
    [OdooModel("mail.thread")]
    public interface IMailThread : IOdooRecord
    {
        [OdooField("message_is_follower")]
        bool IsFollower { get; }

        [OdooField("message_ids")]
        int[] MessageIds { get; }

        [OdooField("message_follower_ids")]
        int[] FollowerIds { get; }
    }

    // --- Mixin 2: Address Info ---
    /// <summary>
    /// Abstract mixin (no specific Odoo model name) for address-related fields.
    /// Used for composition in models that need address information.
    /// </summary>
    public interface IAddress : IOdooRecord
    {
        [OdooField("street")]
        string Street { get; set; }

        [OdooField("street2")]
        string? Street2 { get; set; }

        [OdooField("city")]
        string City { get; set; }

        [OdooField("zip")]
        string? ZipCode { get; set; }

        [OdooField("country_id")]
        int? CountryId { get; set; }

        [OdooField("state_id")]
        int? StateId { get; set; }
    }

    // --- Mixin 3: Contact Info ---
    /// <summary>
    /// Mixin for contact-related fields.
    /// </summary>
    public interface IContactInfo : IOdooRecord
    {
        [OdooField("phone")]
        string? Phone { get; set; }

        [OdooField("mobile")]
        string? Mobile { get; set; }

        [OdooField("email")]
        string? Email { get; set; }

        [OdooField("website")]
        string? Website { get; set; }
    }

    // --- Concrete Model: Partner ---
    /// <summary>
    /// Represents a partner (customer, supplier, contact, etc.).
    /// Inherits functionality from multiple mixins demonstrating multiple inheritance.
    /// </summary>
    [OdooModel("res.partner")]
    public interface IPartner : IMailThread, IAddress, IContactInfo
    {
        [OdooField("name")]
        string Name { get; set; }

        [OdooField("is_company")]
        bool IsCompany { get; set; }

        [OdooField("parent_id")]
        int? ParentId { get; set; }

        [OdooField("child_ids")]
        int[] ChildIds { get; }

        [OdooField("company_id")]
        int? CompanyId { get; set; }

        [OdooField("user_id")]
        int? UserId { get; set; }

        [OdooField("vat")]
        string? VatNumber { get; set; }

        [OdooField("comment")]
        string? Comment { get; set; }

        [OdooField("active")]
        bool Active { get; set; }
    }

    // --- Concrete Model: User ---
    /// <summary>
    /// Represents a system user.
    /// Inherits from IPartner, getting all partner fields plus user-specific ones.
    /// </summary>
    [OdooModel("res.users")]
    public interface IUser : IPartner
    {
        [OdooField("login")]
        string Login { get; set; }

        [OdooField("password")]
        string? Password { get; set; }

        [OdooField("signature")]
        string? Signature { get; set; }

        [OdooField("groups_id")]
        int[] GroupIds { get; }

        [OdooField("company_ids")]
        int[] CompanyIds { get; }

        [OdooField("share")]
        bool IsPortalUser { get; }
    }

    // --- Concrete Model: Company ---
    /// <summary>
    /// Represents a company in the system.
    /// Inherits partner capabilities for address and contact info.
    /// </summary>
    [OdooModel("res.company")]
    public interface ICompany : IPartner
    {
        [OdooField("currency_id")]
        int CurrencyId { get; set; }

        [OdooField("company_parent_id")]
        int? ParentCompanyId { get; set; }

        [OdooField("company_child_ids")]
        int[] ChildCompanyIds { get; }

        [OdooField("logo")]
        byte[]? Logo { get; set; }

        [OdooField("report_header")]
        string? ReportHeader { get; set; }

        [OdooField("report_footer")]
        string? ReportFooter { get; set; }
    }

    // --- Concrete Model: Product ---
    /// <summary>
    /// Represents a product or service.
    /// </summary>
    [OdooModel("product.product")]
    public interface IProduct : IOdooRecord
    {
        [OdooField("name")]
        string Name { get; set; }

        [OdooField("default_code")]
        string? InternalReference { get; set; }

        [OdooField("type")]
        string ProductType { get; set; }

        [OdooField("list_price")]
        decimal ListPrice { get; set; }

        [OdooField("standard_price")]
        decimal Cost { get; set; }

        [OdooField("categ_id")]
        int CategoryId { get; set; }

        [OdooField("uom_id")]
        int UnitOfMeasureId { get; set; }

        [OdooField("active")]
        bool Active { get; set; }

        [OdooField("sale_ok")]
        bool CanBeSold { get; set; }

        [OdooField("purchase_ok")]
        bool CanBePurchased { get; set; }
    }

    // --- Concrete Model: Sale Order ---
    /// <summary>
    /// Represents a sales order.
    /// Inherits mail thread for message tracking.
    /// </summary>
    [OdooModel("sale.order")]
    public interface ISaleOrder : IMailThread
    {
        [OdooField("name")]
        string OrderNumber { get; }

        [OdooField("partner_id")]
        int PartnerId { get; set; }

        [OdooField("date_order")]
        DateTime OrderDate { get; set; }

        [OdooField("state")]
        string State { get; }

        [OdooField("amount_total")]
        decimal TotalAmount { get; }

        [OdooField("amount_tax")]
        decimal TaxAmount { get; }

        [OdooField("amount_untaxed")]
        decimal UntaxedAmount { get; }

        [OdooField("order_line")]
        int[] OrderLineIds { get; }

        [OdooField("user_id")]
        int? SalespersonId { get; set; }

        [OdooField("company_id")]
        int CompanyId { get; set; }

        [OdooField("currency_id")]
        int CurrencyId { get; set; }
    }

    // --- Concrete Model: Sale Order Line ---
    /// <summary>
    /// Represents a line item in a sales order.
    /// </summary>
    [OdooModel("sale.order.line")]
    public interface ISaleOrderLine : IOdooRecord
    {
        [OdooField("order_id")]
        int OrderId { get; set; }

        [OdooField("product_id")]
        int ProductId { get; set; }

        [OdooField("name")]
        string Description { get; set; }

        [OdooField("product_uom_qty")]
        decimal Quantity { get; set; }

        [OdooField("price_unit")]
        decimal UnitPrice { get; set; }

        [OdooField("price_subtotal")]
        decimal Subtotal { get; }

        [OdooField("price_total")]
        decimal Total { get; }

        [OdooField("tax_id")]
        int[] TaxIds { get; set; }

        [OdooField("discount")]
        decimal Discount { get; set; }
    }
}
using System;
using Odoo.Core;
// Models defined in the Demo project - source generator creates typed code for these
using Odoo.Models;
using Odoo.Models.Generated;
// Import extension methods for Demo models (Partner, Product, etc.)
using Odoo.Generated.OdooDemo;
// Alias for Demo ModelSchema to avoid ambiguity
using DemoSchema = Odoo.Generated.OdooDemo.ModelSchema;
// Import typed access from the addons (base and sale)
using Odoo.Base.Models;
using Odoo.Base.Models.Generated;
// Import extension methods for addon types (PartnerBase, PartnerSaleExtension)
using Odoo.Generated.OdooBase;
using BaseSchema = Odoo.Generated.OdooBase.ModelSchema;
using Odoo.Sale.Models;
using Odoo.Sale.Models.Generated;
using Odoo.Generated.OdooSale;
using SaleSchema = Odoo.Generated.OdooSale.ModelSchema;

namespace Odoo.Examples
{
    /// <summary>
    /// Demonstrates all the typed goodies available when using the Odoo Source Generator.
    /// 
    /// When you reference the source generator, it automatically creates:
    /// - ModelSchema: Static class with compile-time tokens for models and fields
    /// - {Model}Record: Strongly-typed struct implementing the model interface
    /// - {Model}Values: Struct for typed record creation
    /// - {Model}BatchContext: Ref struct for efficient batch iteration
    /// - OdooEnvironmentExtensions: Extension methods for typed access
    /// </summary>
    public class TypedApiDemo
    {
        public static void RunDemo()
        {
            Console.WriteLine("=== Source Generator Typed API Demo ===\n");
            Console.WriteLine("This demo showcases all the compile-time type safety you get");
            Console.WriteLine("when using the Odoo Source Generator in your project.\n");

            // Create environment
            var env = new OdooEnvironment(userId: 1);

            // ═══════════════════════════════════════════════════════════════════
            // 1. TYPED SCHEMA ACCESS - Compile-time tokens eliminate string hashing
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  1. TYPED SCHEMA ACCESS (ModelSchema)                       ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            
            Console.WriteLine("Generated ModelSchema provides compile-time tokens:");
            Console.WriteLine($"  DemoSchema.Partner.ModelToken = {DemoSchema.Partner.ModelToken}");
            Console.WriteLine($"  DemoSchema.Partner.Name       = {DemoSchema.Partner.Name}");
            Console.WriteLine($"  DemoSchema.Partner.Email      = {DemoSchema.Partner.Email}");
            Console.WriteLine($"  DemoSchema.Partner.IsCompany  = {DemoSchema.Partner.IsCompany}");
            Console.WriteLine();
            Console.WriteLine($"  DemoSchema.Product.ModelToken = {DemoSchema.Product.ModelToken}");
            Console.WriteLine($"  DemoSchema.Product.Name       = {DemoSchema.Product.Name}");
            Console.WriteLine($"  DemoSchema.Product.ListPrice  = {DemoSchema.Product.ListPrice}");
            Console.WriteLine();

            // Seed some data using typed tokens
            SeedTypedData(env.Columns);

            // ═══════════════════════════════════════════════════════════════════
            // 2. TYPED RECORD ACCESS - Extension methods on IEnvironment
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  2. TYPED RECORD ACCESS (env.Partner(id))                   ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            // Single record access - fully typed!
            var partner = env.Partner(100);
            Console.WriteLine("Single record access with full IntelliSense:");
            Console.WriteLine($"  var partner = env.Partner(100);");
            Console.WriteLine($"  partner.Name       => \"{partner.Name}\"");
            Console.WriteLine($"  partner.Email      => \"{partner.Email}\"");
            Console.WriteLine($"  partner.IsCompany  => {partner.IsCompany}");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // 3. TYPED RECORDSETS - Collection operations with type safety
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  3. TYPED RECORDSETS (env.Partners(ids))                    ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            // RecordSet access - typed collection
            var partners = env.Partners(new[] { 100, 101, 102 });
            Console.WriteLine("RecordSet with typed iteration:");
            Console.WriteLine($"  var partners = env.Partners(new[] {{ 100, 101, 102 }});");
            Console.WriteLine($"  partners.Count => {partners.Count}");
            Console.WriteLine();
            
            foreach (var p in partners)
            {
                Console.WriteLine($"  - {p.Name} (ID: {p.Id}) - Company: {p.IsCompany}");
            }
            Console.WriteLine();

            // Type-safe filtering
            Console.WriteLine("Type-safe LINQ filtering:");
            Console.WriteLine($"  var companies = partners.Where(p => p.IsCompany);");
            var companies = partners.Where(p => p.IsCompany);
            Console.WriteLine($"  Companies found: {companies.Count}");
            foreach (var c in companies)
            {
                Console.WriteLine($"    - {c.Name}");
            }
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // 4. TYPED RECORD CREATION - PartnerValues struct
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  4. TYPED RECORD CREATION (env.Create(PartnerValues))       ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("Create records with compile-time validated values:");
            Console.WriteLine("  var newPartner = env.Create(new PartnerValues");
            Console.WriteLine("  {");
            Console.WriteLine("      Name = \"ACME Corporation\",");
            Console.WriteLine("      Email = \"contact@acme.com\",");
            Console.WriteLine("      IsCompany = true,");
            Console.WriteLine("      Active = true");
            Console.WriteLine("  });");
            Console.WriteLine();

            var newPartner = env.Create(new PartnerValues
            {
                Name = "ACME Corporation",
                Email = "contact@acme.com",
                IsCompany = true,
                Active = true
            });

            Console.WriteLine($"Created partner ID: {newPartner.Id}");
            Console.WriteLine($"  Name:      {newPartner.Name}");
            Console.WriteLine($"  Email:     {newPartner.Email}");
            Console.WriteLine($"  IsCompany: {newPartner.IsCompany}");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // 5. TYPED PROPERTY MODIFICATION - Automatic dirty tracking
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  5. TYPED PROPERTY MODIFICATION (with dirty tracking)       ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("Modify properties with full IntelliSense:");
            Console.WriteLine("  partner.Email = \"new.email@company.com\";");
            Console.WriteLine("  partner.Street = \"123 Main St\";");
            Console.WriteLine();
            
            var modifyPartner = env.Partner(100);
            modifyPartner.Email = "new.email@company.com";
            modifyPartner.Street = "123 Main St";

            Console.WriteLine($"Updated partner:");
            Console.WriteLine($"  Email:  {modifyPartner.Email}");
            Console.WriteLine($"  Street: {modifyPartner.Street}");
            Console.WriteLine();

            // Show dirty tracking
            var dirtyFields = env.Columns.GetDirtyFields(DemoSchema.Partner.ModelToken, 100);
            Console.WriteLine("Dirty tracking (modified fields):");
            foreach (var field in dirtyFields)
            {
                Console.WriteLine($"  - Field token: {field.Token}");
            }
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // 6. TYPED COLUMNAR ACCESS - Direct cache operations
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  6. TYPED COLUMNAR ACCESS (for advanced scenarios)          ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("Direct columnar cache access with typed tokens:");
            Console.WriteLine("  var email = env.Columns.GetValue<string>(");
            Console.WriteLine("      DemoSchema.Partner.ModelToken,");
            Console.WriteLine("      100,");
            Console.WriteLine("      DemoSchema.Partner.Email);");
            Console.WriteLine();

            var email = env.Columns.GetValue<string>(
                DemoSchema.Partner.ModelToken,
                100,
                DemoSchema.Partner.Email);
            Console.WriteLine($"  Result: \"{email}\"");
            Console.WriteLine();

            // Batch columnar access
            Console.WriteLine("Batch columnar access for high-performance scenarios:");
            Console.WriteLine("  var ids = new[] { 100, 101, 102 };");
            Console.WriteLine("  var names = env.Columns.GetColumnSpan<string>(");
            Console.WriteLine("      DemoSchema.Partner.ModelToken,");
            Console.WriteLine("      ids,");
            Console.WriteLine("      DemoSchema.Partner.Name);");
            Console.WriteLine();

            var ids = new[] { 100, 101, 102 };
            var names = env.Columns.GetColumnSpan<string>(
                DemoSchema.Partner.ModelToken,
                ids,
                DemoSchema.Partner.Name);

            Console.WriteLine($"  Batch results ({names.Length} values):");
            for (int i = 0; i < names.Length; i++)
            {
                Console.WriteLine($"    [{ids[i]}] = \"{names[i]}\"");
            }
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // 7. MULTIPLE MODEL TYPES - Works for all OdooModel interfaces
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  7. MULTIPLE MODEL TYPES                                    ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            // Seed product data
            SeedProductData(env.Columns);

            Console.WriteLine("All models with [OdooModel] attribute get generated code:");
            Console.WriteLine();
            
            Console.WriteLine("  // Product operations");
            var product = env.Product(200);
            Console.WriteLine($"  var product = env.Product(200);");
            Console.WriteLine($"  product.Name => \"{product.Name}\"");
            Console.WriteLine($"  product.ListPrice => {product.ListPrice}");
            Console.WriteLine($"  product.ProductType => \"{product.ProductType}\"");
            Console.WriteLine();

            Console.WriteLine("  // Create typed product");
            var newProduct = env.Create(new ProductValues
            {
                Name = "Widget Pro",
                ListPrice = 99.99m,
                Cost = 45.00m,
                ProductType = "consu",
                CanBeSold = true,
                Active = true
            });
            Console.WriteLine($"  Created product: {newProduct.Name} (ID: {newProduct.Id})");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // 8. ADDON-EXTENDED TYPES - Compound types from multiple addons
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  8. ADDON-EXTENDED TYPES (res.partner with sale fields)     ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            
            Console.WriteLine("When you reference addon modules at compile-time, you get typed");
            Console.WriteLine("access to their extended fields as well!");
            Console.WriteLine();
            
            Console.WriteLine("  // Odoo.Base defines IPartnerBase:");
            Console.WriteLine("  //   - Name, Email, IsCompany");
            Console.WriteLine("  // Odoo.Sale extends it with IPartnerSaleExtension:");
            Console.WriteLine("  //   - IsCustomer, CreditLimit");
            Console.WriteLine();

            // Seed extended partner data
            SeedExtendedPartnerData(env.Columns);

            Console.WriteLine("  === Using IPartnerBase (from Odoo.Base) ===");
            Console.WriteLine();
            Console.WriteLine("  var basePartner = env.PartnerBase(500);");
            var basePartner = env.PartnerBase(500);
            Console.WriteLine($"  basePartner.Name      => \"{basePartner.Name}\"");
            Console.WriteLine($"  basePartner.Email     => \"{basePartner.Email}\"");
            Console.WriteLine($"  basePartner.IsCompany => {basePartner.IsCompany}");
            Console.WriteLine();

            Console.WriteLine("  === Using IPartnerSaleExtension (from Odoo.Sale) ===");
            Console.WriteLine("  This type inherits IPartnerBase, so it has ALL the fields!");
            Console.WriteLine();
            Console.WriteLine("  var salePartner = env.PartnerSaleExtension(500);");
            var salePartner = env.PartnerSaleExtension(500);
            Console.WriteLine($"  // Base fields:");
            Console.WriteLine($"  salePartner.Name        => \"{salePartner.Name}\"");
            Console.WriteLine($"  salePartner.Email       => \"{salePartner.Email}\"");
            Console.WriteLine($"  salePartner.IsCompany   => {salePartner.IsCompany}");
            Console.WriteLine($"  // Sale-specific fields:");
            Console.WriteLine($"  salePartner.IsCustomer  => {salePartner.IsCustomer}");
            Console.WriteLine($"  salePartner.CreditLimit => {salePartner.CreditLimit}");
            Console.WriteLine();

            Console.WriteLine("  === Creating with Extended Values ===");
            Console.WriteLine();
            Console.WriteLine("  var newSalePartner = env.Create(new PartnerSaleExtensionValues");
            Console.WriteLine("  {");
            Console.WriteLine("      Name = \"Big Customer Corp\",");
            Console.WriteLine("      Email = \"sales@bigcustomer.com\",");
            Console.WriteLine("      IsCompany = true,");
            Console.WriteLine("      IsCustomer = true,");
            Console.WriteLine("      CreditLimit = 50000.00m");
            Console.WriteLine("  });");
            Console.WriteLine();
            
            var newSalePartner = env.Create(new PartnerSaleExtensionValues
            {
                Name = "Big Customer Corp",
                Email = "sales@bigcustomer.com",
                IsCompany = true,
                IsCustomer = true,
                CreditLimit = 50000.00m
            });
            
            Console.WriteLine($"  Created ID: {newSalePartner.Id}");
            Console.WriteLine($"  newSalePartner.Name        => \"{newSalePartner.Name}\"");
            Console.WriteLine($"  newSalePartner.IsCustomer  => {newSalePartner.IsCustomer}");
            Console.WriteLine($"  newSalePartner.CreditLimit => {newSalePartner.CreditLimit}");
            Console.WriteLine();

            Console.WriteLine("  === Modifying Sale-Specific Fields ===");
            Console.WriteLine();
            Console.WriteLine("  salePartner.CreditLimit = 75000.00m;");
            salePartner.CreditLimit = 75000.00m;
            Console.WriteLine($"  salePartner.CreditLimit (after update) => {salePartner.CreditLimit}");
            Console.WriteLine();

            Console.WriteLine("  === RecordSet with Extended Type ===");
            Console.WriteLine();
            var salePartners = env.PartnerSaleExtensions(new[] { 500, newSalePartner.Id });
            Console.WriteLine($"  var salePartners = env.PartnerSaleExtensions(new[] {{ 500, {newSalePartner.Id} }});");
            Console.WriteLine($"  salePartners.Count => {salePartners.Count}");
            Console.WriteLine();
            
            Console.WriteLine("  // Filter by sale-specific field:");
            Console.WriteLine("  var highCredit = salePartners.Where(p => p.CreditLimit > 10000);");
            var highCredit = salePartners.Where(p => p.CreditLimit > 10000);
            Console.WriteLine($"  highCredit.Count => {highCredit.Count}");
            foreach (var p in highCredit)
            {
                Console.WriteLine($"    - {p.Name}: Credit Limit = {p.CreditLimit}");
            }
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // SUMMARY
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  SUMMARY: What the Source Generator Provides                ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine("  ✓ ModelSchema.{Model}       - Compile-time tokens");
            Console.WriteLine("  ✓ env.{Model}(id)           - Typed single record access");
            Console.WriteLine("  ✓ env.{Models}(ids)         - Typed RecordSet access");
            Console.WriteLine("  ✓ env.Create({Model}Values) - Typed record creation");
            Console.WriteLine("  ✓ record.{Property}         - Typed property access");
            Console.WriteLine("  ✓ {Model}BatchContext       - Efficient batch iteration");
            Console.WriteLine("  ✓ Full IntelliSense         - IDE auto-completion");
            Console.WriteLine("  ✓ Compile-time validation   - Catch errors at build time");
            Console.WriteLine();
            Console.WriteLine("  ✓ ADDON EXTENSIONS:");
            Console.WriteLine("    - Reference addon at compile-time to get typed access");
            Console.WriteLine("    - Extended interfaces (e.g., IPartnerSaleExtension)");
            Console.WriteLine("    - Access both base AND extended fields with type safety");
            Console.WriteLine("    - Same record, different views (IPartnerBase vs IPartnerSaleExtension)");
            Console.WriteLine();
            Console.WriteLine("=== Demo Complete ===");
        }

        private static void SeedTypedData(IColumnarCache cache)
        {
            // Use typed schema tokens for seeding data
            var names = new Dictionary<int, string>
            {
                [100] = "Odoo S.A.",
                [101] = "Mitchell Admin",
                [102] = "Azure Interior"
            };
            cache.BulkLoad(DemoSchema.Partner.ModelToken, DemoSchema.Partner.Name, names);

            var emails = new Dictionary<int, string?>
            {
                [100] = "info@odoo.com",
                [101] = "admin@example.com",
                [102] = "azure@example.com"
            };
            cache.BulkLoad(DemoSchema.Partner.ModelToken, DemoSchema.Partner.Email, emails);

            var isCompany = new Dictionary<int, bool>
            {
                [100] = true,
                [101] = false,
                [102] = true
            };
            cache.BulkLoad(DemoSchema.Partner.ModelToken, DemoSchema.Partner.IsCompany, isCompany);

            var streets = new Dictionary<int, string>
            {
                [100] = "Avenue de Tervueren 421",
                [101] = "215 Vine St",
                [102] = "4557 De Silva St"
            };
            cache.BulkLoad(DemoSchema.Partner.ModelToken, DemoSchema.Partner.Street, streets);

            var cities = new Dictionary<int, string>
            {
                [100] = "Brussels",
                [101] = "Portland",
                [102] = "Fremont"
            };
            cache.BulkLoad(DemoSchema.Partner.ModelToken, DemoSchema.Partner.City, cities);
        }

        private static void SeedProductData(IColumnarCache cache)
        {
            var productNames = new Dictionary<int, string>
            {
                [200] = "Office Chair",
                [201] = "Desk Lamp",
                [202] = "Keyboard"
            };
            cache.BulkLoad(DemoSchema.Product.ModelToken, DemoSchema.Product.Name, productNames);

            var prices = new Dictionary<int, decimal>
            {
                [200] = 299.99m,
                [201] = 49.99m,
                [202] = 79.99m
            };
            cache.BulkLoad(DemoSchema.Product.ModelToken, DemoSchema.Product.ListPrice, prices);

            var types = new Dictionary<int, string>
            {
                [200] = "consu",
                [201] = "consu",
                [202] = "consu"
            };
            cache.BulkLoad(DemoSchema.Product.ModelToken, DemoSchema.Product.ProductType, types);
        }

        private static void SeedExtendedPartnerData(IColumnarCache cache)
        {
            // Use typed schema tokens from Odoo.Base for base fields
            var names = new Dictionary<int, string>
            {
                [500] = "Premier Solutions Inc"
            };
            cache.BulkLoad(Odoo.Generated.OdooBase.ModelSchema.PartnerBase.ModelToken,
                Odoo.Generated.OdooBase.ModelSchema.PartnerBase.Name, names);

            var emails = new Dictionary<int, string?>
            {
                [500] = "contact@premiersolutions.com"
            };
            cache.BulkLoad(Odoo.Generated.OdooBase.ModelSchema.PartnerBase.ModelToken,
                Odoo.Generated.OdooBase.ModelSchema.PartnerBase.Email, emails);

            var isCompany = new Dictionary<int, bool>
            {
                [500] = true
            };
            cache.BulkLoad(Odoo.Generated.OdooBase.ModelSchema.PartnerBase.ModelToken,
                Odoo.Generated.OdooBase.ModelSchema.PartnerBase.IsCompany, isCompany);

            // Use typed schema tokens from Odoo.Sale for sale-specific fields
            var isCustomer = new Dictionary<int, bool>
            {
                [500] = true
            };
            cache.BulkLoad(Odoo.Generated.OdooSale.ModelSchema.PartnerSaleExtension.ModelToken,
                Odoo.Generated.OdooSale.ModelSchema.PartnerSaleExtension.IsCustomer, isCustomer);

            var creditLimit = new Dictionary<int, decimal>
            {
                [500] = 25000.00m
            };
            cache.BulkLoad(Odoo.Generated.OdooSale.ModelSchema.PartnerSaleExtension.ModelToken,
                Odoo.Generated.OdooSale.ModelSchema.PartnerSaleExtension.CreditLimit, creditLimit);
        }
    }
}
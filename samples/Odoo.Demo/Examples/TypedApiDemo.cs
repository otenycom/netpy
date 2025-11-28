using System;
using System.Linq;
using System.Reflection;
using Odoo.Core;
using Odoo.Core.Pipeline;
using Odoo.Core.Modules;
// Models defined in the Demo project - source generator creates typed code for these
using Odoo.Models;
// Import unified wrappers and schema from Demo (uses model name -> ResPartner, Product)
using Odoo.Generated.OdooDemo;
using ModelSchema = Odoo.Generated.OdooDemo.ModelSchema;
// Import typed access from the addons (base and sale)
using Odoo.Base.Models;
using Odoo.Sale.Models;

namespace Odoo.Examples
{
    /// <summary>
    /// Demonstrates all the typed goodies available when using the Odoo Source Generator.
    /// 
    /// New Unified Wrapper Architecture:
    /// - ModelSchema.{ClassName}: Static class with compile-time tokens (e.g., ModelSchema.ResPartner)
    /// - env.GetRecord&lt;T&gt;(id): Get a single record from identity map
    /// - env.GetRecords&lt;T&gt;(ids): Get multiple records as RecordSet
    /// - env.Create({Model}Values): Create a new record
    /// - {Model}BatchContext: Ref struct for efficient batch iteration
    /// </summary>
    public class TypedApiDemo
    {
        public static void RunDemo()
        {
            Console.WriteLine("=== Source Generator Typed API Demo ===\n");
            Console.WriteLine("This demo showcases all the compile-time type safety you get");
            Console.WriteLine("when using the Odoo Source Generator in your project.\n");

            // Create environment with registry
            var registryBuilder = new RegistryBuilder();
            var pipelineRegistry = new PipelineRegistry();
            
            var assemblies = new[]
            {
                typeof(IPartnerBase).Assembly,
                typeof(IPartnerSaleExtension).Assembly,
                typeof(TypedApiDemo).Assembly
            };

            foreach (var assembly in assemblies)
            {
                registryBuilder.ScanAssembly(assembly);
            }
            
            var modelRegistry = registryBuilder.Build();

            foreach (var assembly in assemblies)
            {
                 var registrars = assembly.GetTypes()
                    .Where(t => typeof(IModuleRegistrar).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
                
                foreach (var registrarType in registrars)
                {
                    var registrar = (IModuleRegistrar)Activator.CreateInstance(registrarType)!;
                    registrar.RegisterPipelines(pipelineRegistry);
                    registrar.RegisterFactories(modelRegistry);
                }
            }
            
            pipelineRegistry.CompileAll();

            var env = new OdooEnvironment(userId: 1, modelRegistry: modelRegistry, pipelineRegistry: pipelineRegistry);

            // ═══════════════════════════════════════════════════════════════════
            // 1. TYPED SCHEMA ACCESS - Compile-time tokens eliminate string hashing
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  1. TYPED SCHEMA ACCESS (ModelSchema)                       ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            
            Console.WriteLine("Generated ModelSchema provides compile-time tokens:");
            Console.WriteLine($"  ModelSchema.ResPartner.ModelToken = {ModelSchema.ResPartner.ModelToken}");
            Console.WriteLine($"  ModelSchema.ResPartner.Name       = {ModelSchema.ResPartner.Name}");
            Console.WriteLine($"  ModelSchema.ResPartner.Email      = {ModelSchema.ResPartner.Email}");
            Console.WriteLine($"  ModelSchema.ResPartner.IsCompany  = {ModelSchema.ResPartner.IsCompany}");
            Console.WriteLine();
            Console.WriteLine($"  ModelSchema.ProductProduct.ModelToken = {ModelSchema.ProductProduct.ModelToken}");
            Console.WriteLine($"  ModelSchema.ProductProduct.Name       = {ModelSchema.ProductProduct.Name}");
            Console.WriteLine($"  ModelSchema.ProductProduct.ListPrice  = {ModelSchema.ProductProduct.ListPrice}");
            Console.WriteLine();

            // Seed some data using typed tokens
            SeedTypedData(env.Columns);

            // ═══════════════════════════════════════════════════════════════════
            // 2. TYPED RECORD ACCESS - env.GetRecord<T>(id)
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  2. TYPED RECORD ACCESS (env.GetRecord<T>(id))              ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            // Single record access - fully typed using new API!
            var partner = env.GetRecord<IPartner>("res.partner", 100);
            Console.WriteLine("Single record access with full IntelliSense:");
            Console.WriteLine($"  var partner = env.GetRecord<IPartner>(\"res.partner\", 100);");
            Console.WriteLine($"  partner.Name       => \"{partner.Name}\"");
            Console.WriteLine($"  partner.Email      => \"{partner.Email}\"");
            Console.WriteLine($"  partner.IsCompany  => {partner.IsCompany}");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // 3. TYPED RECORDSETS - env.GetRecords<T>(ids)
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  3. TYPED RECORDSETS (env.GetRecords<T>(ids))               ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            // RecordSet access using new API
            var partners = env.GetRecords<IPartner>("res.partner", new[] { 100, 101, 102 });
            Console.WriteLine("RecordSet with typed iteration:");
            Console.WriteLine($"  var partners = env.GetRecords<IPartner>(\"res.partner\", new[] {{ 100, 101, 102 }});");
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
            // 4. TYPED RECORD CREATION - env.Create(ResPartnerValues)
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  4. TYPED RECORD CREATION (env.Create(ResPartnerValues))    ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("Create records with compile-time validated values:");
            Console.WriteLine("  var newPartner = env.Create(new ResPartnerValues");
            Console.WriteLine("  {");
            Console.WriteLine("      Name = \"ACME Corporation\",");
            Console.WriteLine("      Email = \"contact@acme.com\",");
            Console.WriteLine("      IsCompany = true,");
            Console.WriteLine("      Active = true");
            Console.WriteLine("  });");
            Console.WriteLine();

            var newPartner = env.Create(new ResPartnerValues
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
            // 5. IDENTITY MAP - Same ID returns same instance
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  5. IDENTITY MAP - Reference Equality Guaranteed            ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("The unified wrapper architecture ensures identity:");
            Console.WriteLine("  var p1 = env.GetRecord<IPartner>(\"res.partner\", 100);");
            Console.WriteLine("  var p2 = env.GetRecord<IPartner>(\"res.partner\", 100);");
            Console.WriteLine("  ReferenceEquals(p1, p2) => true // Same instance!");
            Console.WriteLine();
            
            var p1 = env.GetRecord<IPartner>("res.partner", 100);
            var p2 = env.GetRecord<IPartner>("res.partner", 100);
            Console.WriteLine($"  Actual result: ReferenceEquals(p1, p2) => {ReferenceEquals(p1, p2)}");
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
            Console.WriteLine("      ModelSchema.ResPartner.ModelToken,");
            Console.WriteLine("      100,");
            Console.WriteLine("      ModelSchema.ResPartner.Email);");
            Console.WriteLine();

            var email = env.Columns.GetValue<string>(
                ModelSchema.ResPartner.ModelToken,
                100,
                ModelSchema.ResPartner.Email);
            Console.WriteLine($"  Result: \"{email}\"");
            Console.WriteLine();

            // Batch columnar access
            Console.WriteLine("Batch columnar access for high-performance scenarios:");
            Console.WriteLine("  var ids = new[] { 100, 101, 102 };");
            Console.WriteLine("  var names = env.Columns.GetColumnSpan<string>(");
            Console.WriteLine("      ModelSchema.ResPartner.ModelToken,");
            Console.WriteLine("      ids,");
            Console.WriteLine("      ModelSchema.ResPartner.Name);");
            Console.WriteLine();

            var ids = new[] { 100, 101, 102 };
            var names = env.Columns.GetColumnSpan<string>(
                ModelSchema.ResPartner.ModelToken,
                ids,
                ModelSchema.ResPartner.Name);

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
            var product = env.GetRecord<IProduct>("product.product", 200);
            Console.WriteLine($"  var product = env.GetRecord<IProduct>(\"product.product\", 200);");
            Console.WriteLine($"  product.Name => \"{product.Name}\"");
            Console.WriteLine($"  product.ListPrice => {product.ListPrice}");
            Console.WriteLine($"  product.ProductType => \"{product.ProductType}\"");
            Console.WriteLine();

            Console.WriteLine("  // Create typed product");
            var newProduct = env.Create(new ProductProductValues
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
            // 8. ADDON-EXTENDED TYPES - Polymorphism via unified wrappers
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  8. ADDON-EXTENDED TYPES (res.partner with sale fields)     ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            
            Console.WriteLine("When you reference addon modules at compile-time, the unified");
            Console.WriteLine("wrapper implements ALL visible interfaces for that model!");
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
            Console.WriteLine("  var basePartner = env.GetRecord<IPartnerBase>(\"res.partner\", 500);");
            var basePartner = env.GetRecord<IPartnerBase>("res.partner", 500);
            Console.WriteLine($"  basePartner.Name      => \"{basePartner.Name}\"");
            Console.WriteLine($"  basePartner.Email     => \"{basePartner.Email}\"");
            Console.WriteLine($"  basePartner.IsCompany => {basePartner.IsCompany}");
            Console.WriteLine();

            Console.WriteLine("  === Using IPartnerSaleExtension (from Odoo.Sale) ===");
            Console.WriteLine("  Same record, different interface - SAME INSTANCE!");
            Console.WriteLine();
            Console.WriteLine("  var salePartner = env.GetRecord<IPartnerSaleExtension>(\"res.partner\", 500);");
            var salePartner = env.GetRecord<IPartnerSaleExtension>("res.partner", 500);
            Console.WriteLine($"  // Base fields (inherited):");
            Console.WriteLine($"  salePartner.Name        => \"{salePartner.Name}\"");
            Console.WriteLine($"  salePartner.Email       => \"{salePartner.Email}\"");
            Console.WriteLine($"  salePartner.IsCompany   => {salePartner.IsCompany}");
            Console.WriteLine($"  // Sale-specific fields:");
            Console.WriteLine($"  salePartner.IsCustomer  => {salePartner.IsCustomer}");
            Console.WriteLine($"  salePartner.CreditLimit => {salePartner.CreditLimit}");
            Console.WriteLine();

            Console.WriteLine("  === Identity Map Proof ===");
            Console.WriteLine("  ReferenceEquals(basePartner, salePartner) => " + 
                $"{ReferenceEquals(basePartner, salePartner)}");
            Console.WriteLine("  Both interfaces return the SAME unified wrapper instance!");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // SUMMARY
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  SUMMARY: Unified Wrapper Architecture                      ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine("  ✓ ModelSchema.{Model}           - Compile-time tokens");
            Console.WriteLine("  ✓ env.GetRecord<T>(model, id)   - Typed single record access");
            Console.WriteLine("  ✓ env.GetRecords<T>(model, ids) - Typed RecordSet access");
            Console.WriteLine("  ✓ env.Create({Model}Values)     - Typed record creation");
            Console.WriteLine("  ✓ {Model}BatchContext           - Efficient batch iteration");
            Console.WriteLine("  ✓ Identity Map                  - Reference equality guaranteed");
            Console.WriteLine("  ✓ Full IntelliSense             - IDE auto-completion");
            Console.WriteLine("  ✓ Compile-time validation       - Catch errors at build time");
            Console.WriteLine();
            Console.WriteLine("  ✓ UNIFIED WRAPPERS:");
            Console.WriteLine("    - One class per model implementing ALL visible interfaces");
            Console.WriteLine("    - Same instance regardless of which interface you request");
            Console.WriteLine("    - Full polymorphism support across addons");
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
            cache.BulkLoad(ModelSchema.ResPartner.ModelToken, ModelSchema.ResPartner.Name, names);

            var emails = new Dictionary<int, string?>
            {
                [100] = "info@odoo.com",
                [101] = "admin@example.com",
                [102] = "azure@example.com"
            };
            cache.BulkLoad(ModelSchema.ResPartner.ModelToken, ModelSchema.ResPartner.Email, emails);

            var isCompany = new Dictionary<int, bool>
            {
                [100] = true,
                [101] = false,
                [102] = true
            };
            cache.BulkLoad(ModelSchema.ResPartner.ModelToken, ModelSchema.ResPartner.IsCompany, isCompany);

            var streets = new Dictionary<int, string>
            {
                [100] = "Avenue de Tervueren 421",
                [101] = "215 Vine St",
                [102] = "4557 De Silva St"
            };
            cache.BulkLoad(ModelSchema.ResPartner.ModelToken, ModelSchema.ResPartner.Street, streets);

            var cities = new Dictionary<int, string>
            {
                [100] = "Brussels",
                [101] = "Portland",
                [102] = "Fremont"
            };
            cache.BulkLoad(ModelSchema.ResPartner.ModelToken, ModelSchema.ResPartner.City, cities);
        }

        private static void SeedProductData(IColumnarCache cache)
        {
            var productNames = new Dictionary<int, string>
            {
                [200] = "Office Chair",
                [201] = "Desk Lamp",
                [202] = "Keyboard"
            };
            cache.BulkLoad(ModelSchema.ProductProduct.ModelToken, ModelSchema.ProductProduct.Name, productNames);

            var prices = new Dictionary<int, decimal>
            {
                [200] = 299.99m,
                [201] = 49.99m,
                [202] = 79.99m
            };
            cache.BulkLoad(ModelSchema.ProductProduct.ModelToken, ModelSchema.ProductProduct.ListPrice, prices);

            var types = new Dictionary<int, string>
            {
                [200] = "consu",
                [201] = "consu",
                [202] = "consu"
            };
            cache.BulkLoad(ModelSchema.ProductProduct.ModelToken, ModelSchema.ProductProduct.ProductType, types);
        }

        private static void SeedExtendedPartnerData(IColumnarCache cache)
        {
            // Use unified model token for all partner fields
            var names = new Dictionary<int, string>
            {
                [500] = "Premier Solutions Inc"
            };
            cache.BulkLoad(ModelSchema.ResPartner.ModelToken, ModelSchema.ResPartner.Name, names);

            var emails = new Dictionary<int, string?>
            {
                [500] = "contact@premiersolutions.com"
            };
            cache.BulkLoad(ModelSchema.ResPartner.ModelToken, ModelSchema.ResPartner.Email, emails);

            var isCompany = new Dictionary<int, bool>
            {
                [500] = true
            };
            cache.BulkLoad(ModelSchema.ResPartner.ModelToken, ModelSchema.ResPartner.IsCompany, isCompany);

            // Sale-specific fields use same model token (unified model)
            var isCustomer = new Dictionary<int, bool>
            {
                [500] = true
            };
            cache.BulkLoad(ModelSchema.ResPartner.ModelToken, ModelSchema.ResPartner.IsCustomer, isCustomer);

            var creditLimit = new Dictionary<int, decimal>
            {
                [500] = 25000.00m
            };
            cache.BulkLoad(ModelSchema.ResPartner.ModelToken, ModelSchema.ResPartner.CreditLimit, creditLimit);
        }
    }
}
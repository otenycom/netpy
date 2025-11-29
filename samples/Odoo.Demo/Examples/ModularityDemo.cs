using System;
using System.IO;
using System.Linq;
using System.Reflection;
// Import typed access to Odoo.Base models (since we reference it directly)
using Odoo.Base.Models;
using Odoo.Core;
using Odoo.Core.Modules;
using Odoo.Core.Pipeline;
using Odoo.Generated.OdooBase.Logic; // Import generated logic extensions
// Import unified wrappers from Demo (sees cumulative interfaces)
using Odoo.Generated.OdooDemo;
using Odoo.Sale.Models;

namespace Odoo.Examples
{
    public class ModularityDemo
    {
        public static void RunDemo()
        {
            Console.WriteLine("=== Modularity & Pipeline Demo ===\n");

            // 1. Setup paths - navigate up to find the addons folder at the workspace root
            // In production, this would be configured via appsettings.json or environment variable
            string currentDir = Directory.GetCurrentDirectory();
            string addonsPath = Path.Combine(currentDir, "addons");

            // If running from samples/Odoo.Demo/bin/Debug/..., navigate up to find addons
            if (!Directory.Exists(addonsPath))
            {
                // Try to find addons folder by going up directory levels
                var dir = new DirectoryInfo(currentDir);
                while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "addons")))
                {
                    dir = dir.Parent;
                }
                if (dir != null)
                {
                    addonsPath = Path.Combine(dir.FullName, "addons");
                }
            }

            Console.WriteLine($"1. Scanning addons in: {addonsPath}");

            // 2. Initialize ModuleLoader
            var loader = new ModuleLoader(addonsPath);
            var loadedModules = loader.LoadModules();

            Console.WriteLine($"\n2. Loaded {loadedModules.Count} modules:");
            foreach (var mod in loadedModules)
            {
                Console.WriteLine($"   - {mod.Manifest.Name} v{mod.Manifest.Version}");
            }

            // 3. Build Registries
            Console.WriteLine("\n3. Building Registries...");
            var registryBuilder = new RegistryBuilder();
            var pipelineRegistry = new PipelineRegistry();

            foreach (var mod in loadedModules)
            {
                if (mod.Assembly != null)
                {
                    // Scan for models
                    registryBuilder.ScanAssembly(mod.Assembly);
                }
            }

            var modelRegistry = registryBuilder.Build();

            // Register pipelines and factories from each module
            foreach (var mod in loadedModules)
            {
                if (mod.Assembly != null)
                {
                    // Scan for pipeline registrars (generated code)
                    var registrars = mod
                        .Assembly.GetTypes()
                        .Where(t =>
                            typeof(IModuleRegistrar).IsAssignableFrom(t)
                            && !t.IsInterface
                            && !t.IsAbstract
                        );

                    foreach (var registrarType in registrars)
                    {
                        var registrar = (IModuleRegistrar)Activator.CreateInstance(registrarType)!;
                        registrar.RegisterPipelines(pipelineRegistry);
                        registrar.RegisterFactories(modelRegistry);
                        Console.WriteLine(
                            $"   - Registered pipelines & factories from {mod.Manifest.Name}"
                        );
                    }
                }
            }

            pipelineRegistry.CompileAll();

            // 4. Create Environment
            var env = new OdooEnvironment(1, null, modelRegistry, pipelineRegistry);
            Console.WriteLine("\n4. Environment created with merged registries");

            // 5. Show model schema information
            Console.WriteLine("\n5. Merged Model Schema:");
            foreach (var model in modelRegistry.GetAllModels())
            {
                Console.WriteLine($"   Model: {model.ModelName}");
                Console.WriteLine($"     Token: {model.Token}");
                Console.WriteLine(
                    $"     Contributing Interfaces: {model.ContributingInterfaces.Count}"
                );
                foreach (var iface in model.ContributingInterfaces)
                {
                    Console.WriteLine($"       - {iface.FullName}");
                }
                Console.WriteLine($"     Fields: {model.Fields.Count}");
                foreach (var field in model.Fields.Values.OrderBy(f => f.FieldName))
                {
                    Console.WriteLine(
                        $"       - {field.FieldName}: {field.FieldType.Name} [Token: Field({field.Token.Token})] (from {field.ContributingInterface.Name})"
                    );
                }
            }

            // ═══════════════════════════════════════════════════════════════════
            // 6. TYPED vs DYNAMIC API Comparison
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("\n6. Record Creation API Comparison:");
            Console.WriteLine("   ─────────────────────────────────────────────────────────────");

            // APPROACH A: Dynamic API (Pythonic style)
            Console.WriteLine(
                "\n   A) Dynamic/Pythonic API (works with dynamically loaded modules):"
            );
            Console.WriteLine("      var dynamicPartner = env[\"res.partner\"].Create(new {");
            Console.WriteLine("          name = \"Dynamic Partner\",");
            Console.WriteLine("          email = \"dynamic@example.com\"");
            Console.WriteLine("      });");

            var dynamicPartner = env["res.partner"]
                .Create(new { name = "Dynamic Partner", email = "dynamic@example.com" });
            Console.WriteLine($"      → Created ID: {dynamicPartner.Id}");

            // APPROACH B: Strongly-typed API with unified wrappers (read access)
            // Note: When dynamically loading modules, use dynamic Create API,
            // then typed GetRecord<T> API for reading with full type safety.
            Console.WriteLine(
                "\n   B) Strongly-typed Read API (compile-time safety, IntelliSense):"
            );
            Console.WriteLine("      // First create via dynamic API:");
            Console.WriteLine("      var typedRecord = env[\"res.partner\"].Create(new { ... });");
            Console.WriteLine("      // Then access with typed interface:");
            Console.WriteLine(
                "      var typedPartner = env.GetRecord<IPartnerBase>(\"res.partner\", typedRecord.Id);"
            );

            // Create via dynamic API, then access typed
            var typedRecord = env["res.partner"]
                .Create(
                    new
                    {
                        name = "Typed Partner",
                        email = "typed@example.com",
                        is_company = true,
                    }
                );
            var typedPartner = env.GetRecord<IPartnerBase>("res.partner", typedRecord.Id);
            Console.WriteLine($"      → Created ID: {typedPartner.Id}");
            Console.WriteLine($"      → Name: {typedPartner.Name}");
            Console.WriteLine($"      → IsCompany: {typedPartner.IsCompany}");

            // ═══════════════════════════════════════════════════════════════════
            // 7. Typed Record Access & Identity Map
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("\n7. Typed Record Access & Identity Map:");
            Console.WriteLine("   ─────────────────────────────────────────────────────────────");

            Console.WriteLine("\n   // Single record with GetRecord<T>:");
            Console.WriteLine(
                $"   var partner = env.GetRecord<IPartnerBase>(\"res.partner\", {typedPartner.Id});"
            );
            var partner = env.GetRecord<IPartnerBase>("res.partner", typedPartner.Id);
            Console.WriteLine($"   partner.Name      → \"{partner.Name}\"");
            Console.WriteLine($"   partner.Email     → \"{partner.Email}\"");
            Console.WriteLine($"   partner.IsCompany → {partner.IsCompany}");

            Console.WriteLine("\n   // Modify with type safety (Invokes Property Pipeline):");
            Console.WriteLine("   partner.Email = \"updated@example.com\";");
            partner.Email = "updated@example.com";
            Console.WriteLine($"   partner.Email (after update) → \"{partner.Email}\"");

            Console.WriteLine("\n   // Unified Wrapper - Same record via different interfaces:");
            Console.WriteLine("   // The identity map ensures we get the SAME instance!");

            // Get the same record as IPartnerSaleExtension - same unified wrapper instance
            var salePartner = env.GetRecord<IPartnerSaleExtension>("res.partner", typedPartner.Id);
            Console.WriteLine(
                $"   var salePartner = env.GetRecord<IPartnerSaleExtension>(\"res.partner\", {typedPartner.Id});"
            );

            // Use sale-specific fields
            salePartner.IsCustomer = true;
            salePartner.CreditLimit = 5000.50m;

            Console.WriteLine($"   salePartner.IsCustomer → {salePartner.IsCustomer}");
            Console.WriteLine($"   salePartner.CreditLimit → {salePartner.CreditLimit}");
            Console.WriteLine($"   salePartner.Name (base field) → \"{salePartner.Name}\"");

            Console.WriteLine($"\n   // Identity check:");
            Console.WriteLine(
                $"   ReferenceEquals(partner, salePartner) → {ReferenceEquals(partner, salePartner)}"
            );
            Console.WriteLine("   Both interfaces return the SAME unified wrapper instance!");

            // ═══════════════════════════════════════════════════════════════════
            // 8. Typed RecordSet Operations
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("\n8. Typed RecordSet Operations:");
            Console.WriteLine("   ─────────────────────────────────────────────────────────────");

            // Create a few more partners via dynamic API (avoids cross-assembly type issues)
            var partner2Record = env["res.partner"]
                .Create(new { name = "Contact A", is_company = false });
            var partner3Record = env["res.partner"]
                .Create(new { name = "Company B", is_company = true });

            var partners = env.GetRecords<IPartnerBase>(
                "res.partner",
                new[] { typedPartner.Id, partner2Record.Id, partner3Record.Id }
            );
            Console.WriteLine(
                $"\n   var partners = env.GetRecords<IPartnerBase>(\"res.partner\", new[] {{ {typedPartner.Id}, {partner2Record.Id}, {partner3Record.Id} }});"
            );
            Console.WriteLine($"   partners.Count → {partners.Count}");

            Console.WriteLine("\n   // Type-safe LINQ filtering:");
            Console.WriteLine("   var companies = partners.Where(p => p.IsCompany);");
            var companies = partners.Where(p => p.IsCompany);
            Console.WriteLine($"   companies.Count → {companies.Count}");
            foreach (var c in companies)
            {
                Console.WriteLine($"     - {c.Name}");
            }

            // ═══════════════════════════════════════════════════════════════════
            // 9. Execute Pipeline Demo with typed RecordSet
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("\n9. Pipeline Execution:");
            Console.WriteLine("   ─────────────────────────────────────────────────────────────");
            Console.WriteLine("   Expected chain: Sale logic -> Base logic");

            try
            {
                // Create a typed recordset for the pipeline
                var pipelinePartners = env.GetRecords<IPartnerBase>(
                    "res.partner",
                    new[] { typedPartner.Id }
                );

                Console.WriteLine("   Invoking pipeline...");

                // Get the pipeline delegate (it's a void delegate taking RecordSet<IPartnerBase>)
                var pipeline = env.Methods.GetPipeline<Action<RecordSet<IPartnerBase>>>(
                    "res.partner",
                    "action_verify"
                );

                // Invoke directly (no reflection!)
                pipeline(pipelinePartners);

                Console.WriteLine("   ✓ Pipeline executed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   Pipeline execution: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"   Inner: {ex.InnerException.Message}");
            }

            // ═══════════════════════════════════════════════════════════════════
            // Summary
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("\n╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  UNIFIED WRAPPER API SUMMARY                                 ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine("  env.Create(new {Model}Values {{ ... }}) - Typed creation");
            Console.WriteLine("  env.GetRecord<T>(model, id)            - Typed single record");
            Console.WriteLine("  env.GetRecords<T>(model, ids)          - Typed RecordSet");
            Console.WriteLine("  record.{Property}                      - Typed property access");
            Console.WriteLine("  recordset.Where(predicate)             - Type-safe filtering");
            Console.WriteLine();
            Console.WriteLine("  ✓ Identity Map: Same ID always returns same instance");
            Console.WriteLine("  ✓ Unified Wrapper: One class implements ALL interfaces");
            Console.WriteLine(
                "  ✓ Full Polymorphism: IPartnerBase and IPartnerSaleExtension share instance"
            );
            Console.WriteLine();
            Console.WriteLine("  Dynamic API (env[\"model\"].Create(...)) is still available");
            Console.WriteLine("  for interoperability with dynamically loaded modules.");
            Console.WriteLine();
            Console.WriteLine("=== Demo Complete ===");
        }
    }
}

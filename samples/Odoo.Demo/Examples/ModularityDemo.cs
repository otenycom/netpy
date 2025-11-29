using System;
// Import typed access to Odoo.Base models (since we reference it directly)
using Odoo.Base.Models;
using Odoo.Core;
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

            // ═══════════════════════════════════════════════════════════════════
            // 1. Create Environment using OdooEnvironmentBuilder
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("1. Creating OdooEnvironment using OdooEnvironmentBuilder...");
            Console.WriteLine("   The builder automatically:");
            Console.WriteLine("   - Discovers all assemblies referencing Odoo.Core");
            Console.WriteLine("   - Finds IModuleRegistrar implementations");
            Console.WriteLine("   - Scans for [OdooModel] interfaces");
            Console.WriteLine("   - Registers pipelines in dependency order");
            Console.WriteLine("   - Compiles delegate chains\n");

            var env = new OdooEnvironmentBuilder().WithUserId(1).Build();

            Console.WriteLine("   ✓ Environment created successfully!");

            // 2. Show discovered modules (via registry)
            var modelRegistry =
                env.GetType()
                    .GetField(
                        "_modelRegistry",
                        System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Instance
                    )
                    ?.GetValue(env) as Odoo.Core.Modules.ModelRegistry;

            if (modelRegistry != null)
            {
                Console.WriteLine("\n2. Discovered Models:");
                foreach (var model in modelRegistry.GetAllModels())
                {
                    Console.WriteLine($"   - {model.ModelName}");
                    Console.WriteLine($"     Token: {model.Token}");
                    Console.WriteLine(
                        $"     Contributing Interfaces: {model.ContributingInterfaces.Count}"
                    );
                    foreach (var iface in model.ContributingInterfaces)
                    {
                        Console.WriteLine($"       - {iface.FullName}");
                    }
                }
            }

            // ═══════════════════════════════════════════════════════════════════
            // 3. TYPED vs DYNAMIC API Comparison
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("\n3. Record Creation API Comparison:");
            Console.WriteLine("   ─────────────────────────────────────────────────────────────");

            // APPROACH A: Dynamic API (Pythonic style)
            Console.WriteLine("\n   A) Dynamic/Pythonic API (works without compile-time types):");
            Console.WriteLine("      var dynamicPartner = env[\"res.partner\"].Create(new {");
            Console.WriteLine("          name = \"Dynamic Partner\",");
            Console.WriteLine("          email = \"dynamic@example.com\"");
            Console.WriteLine("      });");

            var dynamicPartner = env["res.partner"]
                .Create(new { name = "Dynamic Partner", email = "dynamic@example.com" });
            Console.WriteLine($"      → Created ID: {dynamicPartner.Id}");

            // APPROACH B: Strongly-typed API with unified wrappers (recommended)
            Console.WriteLine(
                "\n   B) Strongly-typed API with OdooEnvironmentBuilder (compile-time safety):"
            );
            Console.WriteLine(
                "      var typedPartner = env.Create(new ResPartnerValues { Name = \"...\", ... });"
            );

            var typedPartner = env.Create(
                new ResPartnerValues
                {
                    Name = "Typed Partner",
                    Email = "typed@example.com",
                    IsCompany = true,
                }
            );
            Console.WriteLine($"      → Created ID: {typedPartner.Id}");
            Console.WriteLine($"      → Name: {typedPartner.Name}");
            Console.WriteLine($"      → IsCompany: {typedPartner.IsCompany}");

            // ═══════════════════════════════════════════════════════════════════
            // 4. Typed Record Access & Identity Map
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("\n4. Typed Record Access & Identity Map:");
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
            // 5. Typed RecordSet Operations
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("\n5. Typed RecordSet Operations:");
            Console.WriteLine("   ─────────────────────────────────────────────────────────────");

            // Create more partners using typed API
            var partner2 = env.Create(
                new ResPartnerValues { Name = "Contact A", IsCompany = false }
            );
            var partner3 = env.Create(
                new ResPartnerValues { Name = "Company B", IsCompany = true }
            );

            var partners = env.GetRecords<IPartnerBase>(
                "res.partner",
                new[] { typedPartner.Id, partner2.Id, partner3.Id }
            );
            Console.WriteLine(
                $"\n   var partners = env.GetRecords<IPartnerBase>(\"res.partner\", new[] {{ {typedPartner.Id}, {partner2.Id}, {partner3.Id} }});"
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
            // 6. Execute Pipeline Demo with typed RecordSet
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("\n6. Pipeline Execution:");
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
            Console.WriteLine("║  STATIC COMPILATION API SUMMARY                              ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine("  // Environment Setup (auto-discovers all addons)");
            Console.WriteLine("  var env = new OdooEnvironmentBuilder()");
            Console.WriteLine("      .WithUserId(1)");
            Console.WriteLine("      .Build();");
            Console.WriteLine();
            Console.WriteLine("  // Typed creation with RecordValueField tracking");
            Console.WriteLine("  env.Create(new ResPartnerValues { Name = \"...\", ... })");
            Console.WriteLine();
            Console.WriteLine("  // Typed record access");
            Console.WriteLine("  env.GetRecord<T>(model, id)");
            Console.WriteLine("  env.GetRecords<T>(model, ids)");
            Console.WriteLine();
            Console.WriteLine("  // Property access invokes pipelines");
            Console.WriteLine("  record.Property = value;  // Invokes Write pipeline");
            Console.WriteLine();
            Console.WriteLine("  ✓ Static Compilation: All types known at compile time");
            Console.WriteLine("  ✓ Identity Map: Same ID always returns same instance");
            Console.WriteLine("  ✓ Unified Wrapper: One class implements ALL interfaces");
            Console.WriteLine(
                "  ✓ Source Generators: Diamond inheritance resolved at compile time"
            );
            Console.WriteLine();
            Console.WriteLine("=== Demo Complete ===");
        }
    }
}

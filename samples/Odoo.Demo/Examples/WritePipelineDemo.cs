using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Odoo.Core;
using Odoo.Core.Pipeline;
using Odoo.Core.Modules;
using Odoo.Models;
using Odoo.Generated.OdooDemo;
using Odoo.Base.Models;
using Odoo.Sale.Models;
using ModelSchema = Odoo.Generated.OdooDemo.ModelSchema;

namespace Odoo.Examples
{
    /// <summary>
    /// Demonstrates the Odoo-aligned Write/Create pipeline architecture.
    /// 
    /// Key Features:
    /// - Property setters delegate to unified Write() pipeline
    /// - Property getters use direct cache access (no pipeline overhead)
    /// - Modules can override Write() to add business logic
    /// - Single extensibility point for all field modifications
    /// </summary>
    public class WritePipelineDemo
    {
        public static void RunDemo()
        {
            Console.WriteLine("=== Write/Create Pipeline Architecture Demo ===\n");
            Console.WriteLine("This demo showcases the Odoo-aligned Write/Create pipeline pattern:");
            Console.WriteLine("  - Property setters → unified Write() pipeline");
            Console.WriteLine("  - Property getters → direct cache read (no pipeline)");
            Console.WriteLine("  - Modules override Write() for business logic\n");

            // Create environment with registry
            var registryBuilder = new RegistryBuilder();
            var pipelineRegistry = new PipelineRegistry();
            
            var assemblies = new[]
            {
                typeof(IPartnerBase).Assembly,      // Odoo.Base
                typeof(IPartnerSaleExtension).Assembly, // Odoo.Sale
                typeof(WritePipelineDemo).Assembly  // Demo project
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
            // 1. CREATE PIPELINE - Record Creation
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  1. CREATE PIPELINE - Record Creation                        ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("Creating a new partner triggers the Create pipeline:");
            Console.WriteLine("  env.Create(new ResPartnerValues { Name = \"Test Corp\" })");
            Console.WriteLine();
            Console.WriteLine("Pipeline chain: [Overrides] → Create_Base");
            Console.WriteLine();

            var partner = env.Create(new ResPartnerValues
            {
                Name = "Test Corporation",
                Email = "test@testcorp.com",
                IsCompany = true,
                Active = true
            });

            Console.WriteLine();
            Console.WriteLine($"Created partner:");
            Console.WriteLine($"  ID:        {partner.Id}");
            Console.WriteLine($"  Name:      {partner.Name}");
            Console.WriteLine($"  Email:     {partner.Email}");
            Console.WriteLine($"  IsCompany: {partner.IsCompany}");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // 2. WRITE PIPELINE - Property Assignment
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  2. WRITE PIPELINE - Property Assignment                     ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("When you assign a property, it goes through the Write pipeline:");
            Console.WriteLine("  partner.Name = \"New Name\"");
            Console.WriteLine("         ↓");
            Console.WriteLine("  Write(handle, { \"name\": \"New Name\" })");
            Console.WriteLine("         ↓");
            Console.WriteLine("  [Sale.Write_SaleOverride] → [Write_Base]");
            Console.WriteLine();

            Console.WriteLine("Setting partner.Email = \"updated@testcorp.com\"...");
            partner.Email = "updated@testcorp.com";
            Console.WriteLine();
            
            Console.WriteLine($"Result: partner.Email = \"{partner.Email}\"");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // 3. DIRECT CACHE READ - Getter Performance
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  3. DIRECT CACHE READ - Getter Performance                   ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("Getters read DIRECTLY from cache (no pipeline overhead):");
            Console.WriteLine("  partner.Name  // → env.Columns.GetValue<string>(...)");
            Console.WriteLine();
            Console.WriteLine("This is aligned with Odoo's Field.__get__() behavior.");
            Console.WriteLine();

            // Demonstrate multiple reads (fast!)
            Console.WriteLine("Reading properties 1000 times (should be very fast)...");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++)
            {
                var _ = partner.Name;
                var __ = partner.Email;
                var ___ = partner.IsCompany;
            }
            sw.Stop();
            Console.WriteLine($"  3000 property reads completed in {sw.ElapsedMilliseconds}ms");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // 4. DIRTY TRACKING - Modified Fields
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  4. DIRTY TRACKING - Modified Fields                         ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("When you modify a field, it's marked as dirty:");
            Console.WriteLine();

            // Clear any existing dirty flags
            env.Columns.ClearDirty(ModelSchema.ResPartner.ModelToken, partner.Id);

            Console.WriteLine("Modifying partner.Name and partner.IsCompany...");
            partner.Name = "Updated Corporation";
            partner.IsCompany = false;

            var dirtyFields = env.Columns.GetDirtyFields(ModelSchema.ResPartner.ModelToken, partner.Id);
            Console.WriteLine($"Dirty fields: {string.Join(", ", dirtyFields.Select(f => $"Field({f.Token})"))}");
            Console.WriteLine();

            Console.WriteLine("These dirty fields would be persisted on env.Flush()");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // 5. FLUSH PATTERN - Database Persistence
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  5. FLUSH PATTERN - Database Persistence                     ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("The Flush() method persists all dirty records to database:");
            Console.WriteLine("  env.Flush();  // Writes all pending changes");
            Console.WriteLine();
            Console.WriteLine("Or flush a specific model:");
            Console.WriteLine("  env.FlushModel(\"res.partner\");");
            Console.WriteLine();
            Console.WriteLine("This is the lazy write pattern from Odoo's ORM.");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // 6. MULTIPLE FIELD WRITE - Batch Efficiency
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  6. MULTIPLE FIELD WRITE - Batch Efficiency                  ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("Each property assignment creates a separate Write call:");
            Console.WriteLine("  partner.Name = \"A\";    // → Write({name: \"A\"})");
            Console.WriteLine("  partner.Email = \"B\";   // → Write({email: \"B\"})");
            Console.WriteLine();
            Console.WriteLine("For efficiency with multiple fields, use the Dictionary API:");
            Console.WriteLine("  // Coming soon: partner.Write(new { Name = \"A\", Email = \"B\" })");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // 7. IDENTITY MAP - Reference Equality
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  7. IDENTITY MAP - Reference Equality                        ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("The same record ID always returns the same instance:");
            var p1 = env.GetRecord<IPartnerBase>("res.partner", partner.Id);
            var p2 = env.GetRecord<IPartnerSaleExtension>("res.partner", partner.Id);

            Console.WriteLine($"  var p1 = env.GetRecord<IPartnerBase>(\"res.partner\", {partner.Id});");
            Console.WriteLine($"  var p2 = env.GetRecord<IPartnerSaleExtension>(\"res.partner\", {partner.Id});");
            Console.WriteLine($"  ReferenceEquals(p1, p2) => {ReferenceEquals(p1, p2)}");
            Console.WriteLine();
            Console.WriteLine("Both interfaces share the same unified wrapper instance!");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // SUMMARY
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  SUMMARY: Odoo-Aligned Architecture                          ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine("  ┌─────────────────────────────────────────────────────┐");
            Console.WriteLine("  │ Odoo Python                │ Our C#                 │");
            Console.WriteLine("  ├─────────────────────────────────────────────────────┤");
            Console.WriteLine("  │ Field.__set__ → write()    │ Setter → Write()       │");
            Console.WriteLine("  │ Field.__get__ → cache      │ Getter → cache         │");
            Console.WriteLine("  │ Override write() method    │ [OdooLogic] override   │");
            Console.WriteLine("  │ @api.depends decorator     │ [OdooDepends] attr     │");
            Console.WriteLine("  │ env.flush()                │ env.Flush()            │");
            Console.WriteLine("  └─────────────────────────────────────────────────────┘");
            Console.WriteLine();
            Console.WriteLine("Key Benefits:");
            Console.WriteLine("  ✓ Single extensibility point (Write pipeline)");
            Console.WriteLine("  ✓ No pipeline overhead on reads (direct cache)");
            Console.WriteLine("  ✓ Lazy persistence with dirty tracking");
            Console.WriteLine("  ✓ Full type safety with compile-time validation");
            Console.WriteLine("  ✓ Same patterns as Odoo for easy migration");
            Console.WriteLine();
            Console.WriteLine("=== Demo Complete ===");
        }
    }
}
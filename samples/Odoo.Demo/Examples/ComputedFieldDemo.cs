using System;
using System.Linq;
using Odoo.Core;
using Odoo.Core.Pipeline;
using Odoo.Core.Modules;
using Odoo.Base.Models;
using Odoo.Sale.Models;
using Odoo.Generated.OdooDemo;
using ModelSchema = Odoo.Generated.OdooDemo.ModelSchema;

namespace Odoo.Examples
{
    /// <summary>
    /// Demonstrates computed fields with the Odoo-aligned batch pattern.
    /// 
    /// Key Features:
    /// - [OdooCompute("_compute_method")] - marks a field as computed
    /// - [OdooDepends("field1", "field2")] - declares dependencies for recomputation
    /// - Compute methods receive RecordSet&lt;T&gt; for batch processing (like Odoo's `for record in self:`)
    /// - SetComputedValue() bypasses the Write pipeline for computed field storage
    /// - Lazy recomputation when dependent fields change
    /// </summary>
    public class ComputedFieldDemo
    {
        public static void RunDemo()
        {
            Console.WriteLine("=== Computed Fields Demo (Odoo-Aligned) ===\n");
            Console.WriteLine("This demo showcases computed fields with automatic recomputation:");
            Console.WriteLine("  - DisplayName is computed from Name and IsCompany");
            Console.WriteLine("  - Changes to dependencies trigger recomputation");
            Console.WriteLine("  - Batch compute pattern matches Odoo's `for record in self:`\n");

            // Create environment with registry
            var registryBuilder = new RegistryBuilder();
            var pipelineRegistry = new PipelineRegistry();
            
            var assemblies = new[]
            {
                typeof(IPartnerBase).Assembly,        // Odoo.Base
                typeof(IPartnerSaleExtension).Assembly, // Odoo.Sale
                typeof(ComputedFieldDemo).Assembly    // Demo project
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
            // 1. COMPUTED FIELD DEFINITION
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  1. COMPUTED FIELD DEFINITION                                ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("In IPartnerBase interface:");
            Console.WriteLine();
            Console.WriteLine("    /// <summary>");
            Console.WriteLine("    /// Computed display name field.");
            Console.WriteLine("    /// Shows \"Name | Company\" for companies, or just \"Name\" for individuals.");
            Console.WriteLine("    /// </summary>");
            Console.WriteLine("    [OdooField(\"display_name\")]");
            Console.WriteLine("    [OdooCompute(\"_compute_display_name\")]");
            Console.WriteLine("    [OdooDepends(\"name\", \"is_company\")]");
            Console.WriteLine("    string DisplayName { get; }  // Read-only computed field");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // 2. COMPUTE METHOD PATTERN
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  2. COMPUTE METHOD PATTERN (Batch RecordSet)                 ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("In PartnerLogic.cs:");
            Console.WriteLine();
            Console.WriteLine("    [OdooLogic(\"res.partner\", \"_compute_display_name\")]");
            Console.WriteLine("    public static void ComputeDisplayName(RecordSet<IPartnerBase> self)");
            Console.WriteLine("    {");
            Console.WriteLine("        foreach (var partner in self)");
            Console.WriteLine("        {");
            Console.WriteLine("            var name = partner.Name ?? \"\";");
            Console.WriteLine("            var displayName = partner.IsCompany");
            Console.WriteLine("                ? $\"{name} | Company\"");
            Console.WriteLine("                : name;");
            Console.WriteLine("            ");
            Console.WriteLine("            // Use SetComputedValue to bypass Write pipeline");
            Console.WriteLine("            env.SetComputedValue(model, partner.Id, field, displayName);");
            Console.WriteLine("        }");
            Console.WriteLine("    }");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // 3. CREATING A PARTNER - INITIAL COMPUTE
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  3. CREATING A PARTNER - Initial Computation                 ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("Creating a new partner...");
            Console.WriteLine("  var partner = env.Create(new ResPartnerValues");
            Console.WriteLine("  {");
            Console.WriteLine("      Name = \"Acme Corporation\",");
            Console.WriteLine("      IsCompany = true");
            Console.WriteLine("  });");
            Console.WriteLine();

            var partner = env.Create(new ResPartnerValues
            {
                Name = "Acme Corporation",
                IsCompany = true,
                Active = true
            });

            Console.WriteLine();
            Console.WriteLine($"Result:");
            Console.WriteLine($"  partner.Id        = {partner.Id}");
            Console.WriteLine($"  partner.Name      = \"{partner.Name}\"");
            Console.WriteLine($"  partner.IsCompany = {partner.IsCompany}");
            // Note: DisplayName access will trigger compute if needed
            // For now, let's manually trigger the compute to show the pattern
            Console.WriteLine();
            Console.WriteLine("Note: DisplayName getter would trigger compute if NeedsRecompute is true.");
            Console.WriteLine("(Full integration with getter triggers coming in next phase)");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // 4. MANUAL COMPUTE TRIGGER (SIMULATION)
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  4. MANUAL COMPUTE TRIGGER (Simulation)                      ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("Simulating what the generated getter would do...");
            Console.WriteLine("  1. Check NeedsRecompute(model, id, field)");
            Console.WriteLine("  2. If true, create RecordSet with this ID");
            Console.WriteLine("  3. Call compute method: _compute_display_name(recordSet)");
            Console.WriteLine("  4. Read value from cache");
            Console.WriteLine();
            
            // Simulate the compute call manually
            var partnerRecordSet = env.GetRecords<IPartnerBase>("res.partner", new[] { partner.Id });
            Console.WriteLine($"Calling ComputeDisplayName for partner {partner.Id}...");
            Console.WriteLine();
            
            // Call the compute method directly (this simulates what the getter would do)
            Odoo.Base.Logic.PartnerLogic.ComputeDisplayName(partnerRecordSet);
            
            Console.WriteLine();
            Console.WriteLine("Reading computed value from cache...");
            
            // Read the computed value using the same token algorithm
            var displayName = GetComputedDisplayName(env, partner.Id);
            Console.WriteLine($"  partner.DisplayName = \"{displayName ?? "(null)"}\"");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // 5. DEPENDENCY TRACKING - Modifying Name
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  5. DEPENDENCY TRACKING - Modifying Name                     ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("When Name changes, DisplayName should be recomputed...");
            Console.WriteLine("  partner.Name = \"Globex Industries\";");
            Console.WriteLine();
            
            partner.Name = "Globex Industries";
            
            Console.WriteLine("In Odoo pattern:");
            Console.WriteLine("  1. Name setter calls Modified(model, id, name_field)");
            Console.WriteLine("  2. ComputeTracker marks DisplayName for recompute (depends on 'name')");
            Console.WriteLine("  3. Next access to DisplayName triggers recomputation");
            Console.WriteLine();
            
            // Simulate recomputation
            Console.WriteLine("Triggering recomputation manually...");
            Odoo.Base.Logic.PartnerLogic.ComputeDisplayName(partnerRecordSet);
            
            displayName = GetComputedDisplayName(env, partner.Id);
            Console.WriteLine();
            Console.WriteLine($"After Name change: DisplayName = \"{displayName ?? "(null)"}\"");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // 6. DEPENDENCY TRACKING - Modifying IsCompany
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  6. DEPENDENCY TRACKING - Modifying IsCompany                ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("Changing IsCompany to false...");
            Console.WriteLine("  partner.IsCompany = false;");
            Console.WriteLine();
            
            partner.IsCompany = false;
            
            // Simulate recomputation
            Console.WriteLine("Triggering recomputation...");
            Odoo.Base.Logic.PartnerLogic.ComputeDisplayName(partnerRecordSet);
            
            displayName = GetComputedDisplayName(env, partner.Id);
            Console.WriteLine();
            Console.WriteLine($"After IsCompany change: DisplayName = \"{displayName ?? "(null)"}\"");
            Console.WriteLine("(No more '| Company' suffix since it's now an individual)");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // 7. BATCH COMPUTATION
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  7. BATCH COMPUTATION - Multiple Records                     ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("Creating multiple partners...");
            
            var partner2 = env.Create(new ResPartnerValues { Name = "Bob Smith", IsCompany = false, Active = true });
            var partner3 = env.Create(new ResPartnerValues { Name = "TechCorp Ltd", IsCompany = true, Active = true });
            var partner4 = env.Create(new ResPartnerValues { Name = "Alice Johnson", IsCompany = false, Active = true });
            
            Console.WriteLine($"  Created: ID={partner2.Id}, Name=\"{partner2.Name}\"");
            Console.WriteLine($"  Created: ID={partner3.Id}, Name=\"{partner3.Name}\"");
            Console.WriteLine($"  Created: ID={partner4.Id}, Name=\"{partner4.Name}\"");
            Console.WriteLine();

            // Create batch recordset
            var allPartners = env.GetRecords<IPartnerBase>("res.partner", 
                new[] { partner.Id, partner2.Id, partner3.Id, partner4.Id });
            
            Console.WriteLine($"Computing DisplayName for {allPartners.Count} partners in a single batch...");
            Console.WriteLine("This is efficient: one method call processes all records.");
            Console.WriteLine();
            
            Odoo.Base.Logic.PartnerLogic.ComputeDisplayName(allPartners);
            
            Console.WriteLine();
            Console.WriteLine("Results after batch computation:");
            foreach (var p in allPartners)
            {
                var dn = GetComputedDisplayName(env, p.Id);
                Console.WriteLine($"  [{p.Id}] {p.Name} -> \"{dn}\"");
            }
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // SUMMARY
            // ═══════════════════════════════════════════════════════════════════
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  SUMMARY: Computed Fields Architecture                       ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine("  ┌─────────────────────────────────────────────────────────┐");
            Console.WriteLine("  │ Odoo Python                │ Our C#                     │");
            Console.WriteLine("  ├─────────────────────────────────────────────────────────┤");
            Console.WriteLine("  │ @api.depends('name')       │ [OdooDepends(\"name\")]      │");
            Console.WriteLine("  │ def _compute_foo(self):    │ [OdooLogic(...)]           │");
            Console.WriteLine("  │     for rec in self:       │ void Compute(RecordSet<T>) │");
            Console.WriteLine("  │         rec.foo = ...      │     foreach (var r in self)│");
            Console.WriteLine("  │                            │     env.SetComputedValue() │");
            Console.WriteLine("  │ compute='_compute_foo'     │ [OdooCompute(\"...\")]       │");
            Console.WriteLine("  └─────────────────────────────────────────────────────────┘");
            Console.WriteLine();
            Console.WriteLine("Key Benefits:");
            Console.WriteLine("  ✓ Same batch pattern as Odoo (for record in self)");
            Console.WriteLine("  ✓ Declarative dependencies with [OdooDepends]");
            Console.WriteLine("  ✓ Lazy recomputation on dependency changes");
            Console.WriteLine("  ✓ SetComputedValue bypasses Write pipeline");
            Console.WriteLine("  ✓ Efficient batch processing for multiple records");
            Console.WriteLine("  ✓ Type-safe with full IntelliSense support");
            Console.WriteLine();
            Console.WriteLine("=== Demo Complete ===");
        }
        
        /// <summary>
        /// Helper to read the computed DisplayName from cache.
        /// Uses the same stable hash algorithm as the source generator.
        /// </summary>
        private static string? GetComputedDisplayName(OdooEnvironment env, int recordId)
        {
            const string modelName = "res.partner";
            const string fieldName = "display_name";
            var modelToken = GetStableHashCode(modelName);
            var fieldToken = GetStableHashCode($"{modelName}.{fieldName}");
            
            return env.Columns.GetValue<string>(
                new ModelHandle(modelToken),
                recordId,
                new FieldHandle(fieldToken));
        }
        
        /// <summary>
        /// Stable hash code algorithm - matches the source generator.
        /// </summary>
        private static int GetStableHashCode(string str)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in str)
                {
                    hash = hash * 31 + c;
                }
                return hash;
            }
        }
    }
}
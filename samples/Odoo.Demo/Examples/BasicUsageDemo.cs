using System;
using System.Linq;
using System.Reflection;
using Odoo.Core;
using Odoo.Core.Pipeline;
using Odoo.Core.Modules;
using Odoo.Base.Models;
using Odoo.Sale.Models;
// Import unified wrappers and schema from the generated code
using Odoo.Generated.OdooDemo;
using Schema = Odoo.Generated.OdooDemo.ModelSchema;

namespace Odoo.Examples
{
    /// <summary>
    /// Demonstrates basic usage of the Odoo ORM in C#.
    /// Uses the new unified wrapper architecture with identity map support.
    /// </summary>
    public class BasicUsageDemo
    {
        public static void RunDemo()
        {
            Console.WriteLine("=== Odoo ORM Basic Usage Demo ===\n");

            // 1. Create an environment with model registry
            var registryBuilder = new RegistryBuilder();
            var pipelineRegistry = new PipelineRegistry();
            
            var assemblies = new[]
            {
                typeof(IPartnerBase).Assembly,      // Odoo.Base
                typeof(IPartnerSaleExtension).Assembly, // Odoo.Sale
                typeof(BasicUsageDemo).Assembly     // Demo project
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

            Console.WriteLine("1. Created environment for user ID: 1\n");

            // 2. Seed some sample data
            SeedSampleData(env.Columns);

            // 3. Access a single partner record using new GetRecord<T> API
            Console.WriteLine("2. Accessing a single partner:");
            var partner = env.GetRecord<IPartnerBase>("res.partner", 10);
            Console.WriteLine($"   Partner Name: {partner.Name}");
            Console.WriteLine($"   Partner Email: {partner.Email}");
            Console.WriteLine($"   Is Company: {partner.IsCompany}");
            Console.WriteLine();

            // 4. Modify a record
            Console.WriteLine("3. Modifying partner data:");
            partner.Email = "updated@odoo.com";
            Console.WriteLine($"   Updated Email: {partner.Email}");
            
            // Show dirty fields
            var dirtyFields = env.Columns.GetDirtyFields(Schema.ResPartner.ModelToken, 10);
            Console.WriteLine($"   Dirty Fields: {string.Join(", ", dirtyFields.Select(f => $"Field({f.Token})"))}");
            Console.WriteLine();

            // 5. Access multiple records (RecordSet) using new GetRecords<T> API
            Console.WriteLine("4. Working with multiple records:");
            var partners = env.GetRecords<IPartnerBase>("res.partner", new[] { 10, 11, 12 });
            Console.WriteLine($"   Total partners: {partners.Count}");
            
            foreach (var p in partners)
            {
                Console.WriteLine($"   - {p.Name} (ID: {p.Id})");
            }
            Console.WriteLine();

            // 6. Filter with LINQ-style operations
            Console.WriteLine("5. Filtering records:");
            var companies = partners.Where(p => p.IsCompany);
            Console.WriteLine($"   Companies found: {companies.Count}");
            foreach (var company in companies)
            {
                Console.WriteLine($"   - {company.Name}");
            }
            Console.WriteLine();

            // 7. Environment with different user
            Console.WriteLine("6. Creating environment for different user:");
            var userEnv = env.WithUser(2);
            Console.WriteLine($"   New environment user ID: {userEnv.UserId}");
            Console.WriteLine($"   Same cache: {userEnv.Columns == env.Columns}");
            Console.WriteLine();

            // 8. Identity map - same ID gives same instance
            Console.WriteLine("7. Identity Map demonstration:");
            var partner1 = env.GetRecord<IPartnerBase>("res.partner", 10);
            var partner2 = env.GetRecord<IPartnerBase>("res.partner", 10);
            Console.WriteLine($"   partner1 == partner2: {ReferenceEquals(partner1, partner2)}");
            Console.WriteLine("   (Same ID returns same object instance via identity map!)");
            Console.WriteLine();

            Console.WriteLine("=== Demo Complete ===");
        }

        private static void SeedSampleData(IColumnarCache cache)
        {
            // Load data using typed BulkLoad with proper schema tokens
            var names = new Dictionary<int, string>
            {
                [10] = "Odoo S.A.",
                [11] = "Mitchell Admin",
                [12] = "Azure Interior"
            };
            cache.BulkLoad(Schema.ResPartner.ModelToken, Schema.ResPartner.Name, names);

            var emails = new Dictionary<int, string?>
            {
                [10] = "info@odoo.com",
                [11] = "admin@example.com",
                [12] = "azure@example.com"
            };
            cache.BulkLoad(Schema.ResPartner.ModelToken, Schema.ResPartner.Email, emails);

            var isCompany = new Dictionary<int, bool>
            {
                [10] = true,
                [11] = false,
                [12] = true
            };
            cache.BulkLoad(Schema.ResPartner.ModelToken, Schema.ResPartner.IsCompany, isCompany);
        }
    }
}
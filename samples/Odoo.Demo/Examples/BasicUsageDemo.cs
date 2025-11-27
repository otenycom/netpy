using System;
using Odoo.Core;
using Odoo.Base.Models;
using Odoo.Base.Models.Generated;
using Odoo.Generated.OdooBase;

namespace Odoo.Examples
{
    /// <summary>
    /// Demonstrates basic usage of the Odoo ORM in C#.
    /// </summary>
    public class BasicUsageDemo
    {
        public static void RunDemo()
        {
            Console.WriteLine("=== Odoo ORM Basic Usage Demo ===\n");

            // 1. Create an environment with columnar cache
            var env = new OdooEnvironment(userId: 1);

            Console.WriteLine("1. Created environment for user ID: 1\n");

            // 2. Seed some sample data
            SeedSampleData(env.Columns);

            // 3. Access a single partner record
            Console.WriteLine("2. Accessing a single partner:");
            var partner = env.PartnerBase(10);
            Console.WriteLine($"   Partner Name: {partner.Name}");
            Console.WriteLine($"   Partner Email: {partner.Email}");
            Console.WriteLine($"   Is Company: {partner.IsCompany}");
            Console.WriteLine();

            // 4. Modify a record
            Console.WriteLine("3. Modifying partner data:");
            partner.Email = "updated@odoo.com";
            Console.WriteLine($"   Updated Email: {partner.Email}");
            
            // Show dirty fields
            var dirtyFields = env.Columns.GetDirtyFieldNames("res.partner", ModelSchema.PartnerBase.ModelToken, 10);
            Console.WriteLine($"   Dirty Fields: {string.Join(", ", dirtyFields)}");
            Console.WriteLine();

            // 5. Access multiple records (RecordSet)
            Console.WriteLine("4. Working with multiple records:");
            var partners = env.PartnerBases(new[] { 10, 11, 12 });
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
            cache.BulkLoad(ModelSchema.PartnerBase.ModelToken, ModelSchema.PartnerBase.Name, names);

            var emails = new Dictionary<int, string?>
            {
                [10] = "info@odoo.com",
                [11] = "admin@example.com",
                [12] = "azure@example.com"
            };
            cache.BulkLoad(ModelSchema.PartnerBase.ModelToken, ModelSchema.PartnerBase.Email, emails);

            var isCompany = new Dictionary<int, bool>
            {
                [10] = true,
                [11] = false,
                [12] = true
            };
            cache.BulkLoad(ModelSchema.PartnerBase.ModelToken, ModelSchema.PartnerBase.IsCompany, isCompany);
        }
    }
}
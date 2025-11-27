using System;
using Odoo.Core;
using Odoo.Models;
using Odoo.Generated;
using Odoo.Python;

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

            // 1. Create an environment
            var cache = new SimpleValueCache();
            var env = new OdooEnvironment(userId: 1, cache: cache);

            Console.WriteLine("1. Created environment for user ID: 1\n");

            // 2. Seed some sample data
            SeedSampleData(cache);

            // 3. Access a single partner record
            Console.WriteLine("2. Accessing a single partner:");
            var partner = env.Partner(10);
            Console.WriteLine($"   Partner Name: {partner.Name}");
            Console.WriteLine($"   Partner Email: {partner.Email}");
            Console.WriteLine($"   Is Company: {partner.IsCompany}");
            Console.WriteLine($"   City: {partner.City}");
            Console.WriteLine();

            // 4. Modify a record
            Console.WriteLine("3. Modifying partner data:");
            partner.City = "Brussels";
            partner.Phone = "+32 2 123 4567";
            Console.WriteLine($"   Updated City: {partner.City}");
            Console.WriteLine($"   Updated Phone: {partner.Phone}");
            
            // Show dirty fields
            var dirtyFields = cache.GetDirtyFields("res.partner", 10);
            Console.WriteLine($"   Dirty Fields: {string.Join(", ", dirtyFields)}");
            Console.WriteLine();

            // 5. Access multiple records (RecordSet)
            Console.WriteLine("4. Working with multiple records:");
            var partners = env.Partners(new[] { 10, 11, 12 });
            Console.WriteLine($"   Total partners: {partners.Count}");
            
            foreach (var p in partners)
            {
                Console.WriteLine($"   - {p.Name} (ID: {p.Id})");
            }
            Console.WriteLine();

            // 6. Demonstrate mixin inheritance
            Console.WriteLine("5. Demonstrating multiple inheritance (mixins):");
            var partner2 = env.Partner(11);
            
            // Access properties from different interfaces
            Console.WriteLine($"   Name (IPartner): {partner2.Name}");
            Console.WriteLine($"   Street (IAddress): {partner2.Street}");
            Console.WriteLine($"   Email (IContactInfo): {partner2.Email}");
            Console.WriteLine($"   IsFollower (IMailThread): {partner2.IsFollower}");
            Console.WriteLine();

            // 7. Filter with LINQ-style operations
            Console.WriteLine("6. Filtering records:");
            var companies = partners.Where(p => p.IsCompany);
            Console.WriteLine($"   Companies found: {companies.Count}");
            foreach (var company in companies)
            {
                Console.WriteLine($"   - {company.Name}");
            }
            Console.WriteLine();

            // 8. Environment with different user
            Console.WriteLine("7. Creating environment for different user:");
            var userEnv = env.WithUser(2);
            Console.WriteLine($"   New environment user ID: {userEnv.UserId}");
            Console.WriteLine($"   Same cache: {userEnv.Cache == env.Cache}");
            Console.WriteLine();

            Console.WriteLine("=== Demo Complete ===");
        }

        private static void SeedSampleData(SimpleValueCache cache)
        {
            // Partner 10 - Odoo S.A.
            cache.BulkLoad("res.partner", new()
            {
                [10] = new()
                {
                    ["name"] = "Odoo S.A.",
                    ["email"] = "info@odoo.com",
                    ["is_company"] = true,
                    ["street"] = "Chaussée de Namur 40",
                    ["city"] = "Ramillies",
                    ["zip"] = "1367",
                    ["phone"] = "+32 81 81 37 00",
                    ["website"] = "https://www.odoo.com",
                    ["active"] = true,
                    ["message_is_follower"] = false,
                    ["message_ids"] = Array.Empty<int>(),
                    ["message_follower_ids"] = Array.Empty<int>(),
                    ["child_ids"] = new[] { 11, 12 }
                },
                [11] = new()
                {
                    ["name"] = "Mitchell Admin",
                    ["email"] = "admin@example.com",
                    ["is_company"] = false,
                    ["parent_id"] = 10,
                    ["street"] = "Chaussée de Namur 40",
                    ["city"] = "Ramillies",
                    ["zip"] = "1367",
                    ["phone"] = "+32 81 81 37 01",
                    ["mobile"] = "+32 475 12 34 56",
                    ["active"] = true,
                    ["message_is_follower"] = true,
                    ["message_ids"] = new[] { 1, 2, 3 },
                    ["message_follower_ids"] = new[] { 1 },
                    ["child_ids"] = Array.Empty<int>()
                },
                [12] = new()
                {
                    ["name"] = "Azure Interior",
                    ["email"] = "azure@example.com",
                    ["is_company"] = true,
                    ["parent_id"] = 10,
                    ["street"] = "Avenue des Dessus-de-Lives 2",
                    ["city"] = "Namur",
                    ["zip"] = "5101",
                    ["phone"] = "+32 81 33 44 55",
                    ["website"] = "https://azure-interior.com",
                    ["active"] = true,
                    ["message_is_follower"] = false,
                    ["message_ids"] = Array.Empty<int>(),
                    ["message_follower_ids"] = Array.Empty<int>(),
                    ["child_ids"] = Array.Empty<int>()
                }
            });
        }
    }
}
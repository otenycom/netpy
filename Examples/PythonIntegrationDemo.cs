using System;
using System.IO;
using Odoo.Core;
using Odoo.Models;
using Odoo.Generated;
using Odoo.Python;

namespace Odoo.Examples
{
    /// <summary>
    /// Demonstrates Python integration with the Odoo ORM.
    /// </summary>
    public class PythonIntegrationDemo
    {
        public static void RunDemo()
        {
            Console.WriteLine("=== Odoo ORM Python Integration Demo ===\n");

            // 1. Create environment and cache
            var cache = new SimpleValueCache();
            var env = new OdooEnvironment(userId: 1, cache: cache);

            // Seed sample data
            SeedSampleData(cache);

            // 2. Create Python module loader
            var scriptsPath = Path.Combine(Directory.GetCurrentDirectory(), "Scripts");
            var moduleLoader = new PythonModuleLoader(scriptsPath);

            Console.WriteLine($"1. Initialized Python module loader");
            Console.WriteLine($"   Scripts path: {scriptsPath}\n");

            // 3. Create Python bridge
            var pythonBridge = new OdooPythonBridge(env, moduleLoader);

            // 4. Load Python module
            Console.WriteLine("2. Loading Python module 'odoo_module_sample':");
            try
            {
                var module = moduleLoader.LoadModule("odoo_module_sample");
                Console.WriteLine("   ✓ Module loaded successfully\n");

                // 5. Call Python function with environment
                Console.WriteLine("3. Calling Python function 'compute_partner_display_name':");
                var displayName = pythonBridge.ExecuteModuleMethod<string>(
                    "odoo_module_sample",
                    "compute_partner_display_name",
                    10);
                Console.WriteLine($"   Result: {displayName}\n");

                // 6. Execute Python workflow
                Console.WriteLine("4. Executing partner approval workflow:");
                var workflowResult = moduleLoader.CallFunction<dynamic>(
                    "odoo_module_sample",
                    "partner_approval_workflow",
                    env,
                    10,
                    "approve");
                Console.WriteLine($"   Partner ID: {workflowResult.partner_id}");
                Console.WriteLine($"   Action: {workflowResult.action}");
                Console.WriteLine($"   New State: {workflowResult.new_state}");
                Console.WriteLine($"   Success: {workflowResult.success}\n");

                // 7. Batch processing with Python
                Console.WriteLine("5. Processing partner batch with Python:");
                var batchResult = moduleLoader.CallFunction<dynamic>(
                    "odoo_module_sample",
                    "process_partner_batch",
                    env,
                    new[] { 10, 11, 12 },
                    "validate");
                Console.WriteLine($"   Processed: {batchResult.processed} partners");
                Console.WriteLine($"   Operation: {batchResult.operation}");
                Console.WriteLine($"   Success: {batchResult.success}\n");

                // 8. Use Python extension methods
                Console.WriteLine("6. Using Python extension methods:");
                var creditScore = moduleLoader.CallFunction<int>(
                    "odoo_module_sample",
                    "PartnerExtension.calculate_credit_score",
                    env,
                    10);
                Console.WriteLine($"   Credit Score: {creditScore}\n");

                // 9. Execute inline Python code
                Console.WriteLine("7. Executing inline Python code:");
                var code = @"
result = f'Environment user: {env.UserId}'
";
                pythonBridge.ExecuteWithEnvironment(code);
                Console.WriteLine("   ✓ Code executed successfully\n");

                Console.WriteLine("=== Python Integration Demo Complete ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ✗ Error: {ex.Message}");
                Console.WriteLine("\n   Note: Python integration requires proper Python setup.");
                Console.WriteLine("   Make sure Python is initialized in the main program.");
            }
        }

        private static void SeedSampleData(SimpleValueCache cache)
        {
            cache.BulkLoad("res.partner", new()
            {
                [10] = new()
                {
                    ["name"] = "Odoo S.A.",
                    ["email"] = "info@odoo.com",
                    ["is_company"] = true,
                    ["street"] = "Chaussée de Namur 40",
                    ["city"] = "Ramillies",
                    ["active"] = true
                },
                [11] = new()
                {
                    ["name"] = "Mitchell Admin",
                    ["email"] = "admin@example.com",
                    ["is_company"] = false,
                    ["parent_id"] = 10,
                    ["active"] = true
                },
                [12] = new()
                {
                    ["name"] = "Azure Interior",
                    ["email"] = "azure@example.com",
                    ["is_company"] = true,
                    ["parent_id"] = 10,
                    ["active"] = true
                }
            });
        }
    }
}
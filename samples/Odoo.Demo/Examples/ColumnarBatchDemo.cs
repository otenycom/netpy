using System;
using System.Diagnostics;
using System.Linq;
using Odoo.Base.Models;
// Import batch context (generated in the namespace of the first interface)
using Odoo.Base.Models.Generated;
using Odoo.Core;
// Import unified wrappers and schema from Demo
using Odoo.Generated.OdooDemo;

namespace Odoo.Examples
{
    /// <summary>
    /// Demonstrates the new Data-Oriented Design optimizations with columnar storage.
    /// Shows both traditional and optimized batch access patterns.
    /// Uses the new unified wrapper architecture with GetRecord<T> and GetRecords<T> APIs.
    /// </summary>
    public class ColumnarBatchDemo
    {
        public static void RunDemo()
        {
            Console.WriteLine("=== Data-Oriented Design (Columnar Storage) Demo ===\n");

            // Create environment with columnar cache
            var env = new OdooEnvironment(userId: 1);

            // Simulate loading some partner data
            LoadSampleData(env);

            // Demo 1: Traditional single-record access using new API
            Console.WriteLine("1. Single Record Access with GetRecord<T>:");
            SingleRecordAccessDemo(env);
            Console.WriteLine();

            // Demo 2: RecordSet iteration using new GetRecords<T> API
            Console.WriteLine("2. RecordSet Iteration with GetRecords<T>:");
            RecordSetIterationDemo(env);
            Console.WriteLine();

            // Demo 3: Optimized batch iteration using BatchContext
            Console.WriteLine("3. Optimized Batch Iteration with BatchContext:");
            OptimizedBatchIterationDemo(env);
            Console.WriteLine();

            // Demo 4: Performance comparison
            Console.WriteLine("4. Performance Comparison:");
            PerformanceComparisonDemo(env);
        }

        private static void LoadSampleData(OdooEnvironment env)
        {
            Console.WriteLine("Loading sample partner data into columnar cache...\n");

            // Simulate bulk loading data (this would normally come from database)
            var partnerIds = Enumerable.Range(1, 100).Select(i => (RecordId)i).ToArray();

            // Load names - using unified schema
            var names = partnerIds.ToDictionary(id => id, id => $"Partner {id.Value}");
            env.Columns.BulkLoad(
                ModelSchema.ResPartner.ModelToken,
                ModelSchema.ResPartner.Name,
                names
            );

            // Load emails
            var emails = partnerIds.ToDictionary(id => id, id => $"partner{id.Value}@example.com");
            env.Columns.BulkLoad(
                ModelSchema.ResPartner.ModelToken,
                ModelSchema.ResPartner.Email,
                emails
            );

            // Load IsCompany flags
            var isCompanyFlags = partnerIds.ToDictionary(
                id => id,
                id => id.Value % 3 == 0 // Every 3rd partner is a company
            );
            env.Columns.BulkLoad(
                ModelSchema.ResPartner.ModelToken,
                ModelSchema.ResPartner.IsCompany,
                isCompanyFlags
            );

            Console.WriteLine($"Loaded {partnerIds.Length} partners into columnar cache\n");
        }

        private static void SingleRecordAccessDemo(OdooEnvironment env)
        {
            // Access a single partner using the new GetRecord<T> API
            RecordId partnerId = 1;
            var partner = env.GetRecord<IPartnerBase>("res.partner", partnerId);

            Console.WriteLine($"  var partner = env.GetRecord<IPartnerBase>(\"res.partner\", 1);");
            Console.WriteLine($"  Partner ID: {partner.Id}");
            Console.WriteLine($"  Name: {partner.Name}");
            Console.WriteLine($"  Email: {partner.Email}");
            Console.WriteLine($"  Is Company: {partner.IsCompany}");

            // Writing still works
            partner.Name = "Updated Partner Name";
            Console.WriteLine($"  Updated Name: {partner.Name}");

            // Identity map demonstration
            var partner2 = env.GetRecord<IPartnerBase>("res.partner", partnerId);
            Console.WriteLine($"  Same instance check: {ReferenceEquals(partner, partner2)}");
        }

        private static void RecordSetIterationDemo(OdooEnvironment env)
        {
            var partnerIds = Enumerable.Range(1, 10).Select(i => (RecordId)i).ToArray();
            var partners = env.GetRecords<IPartnerBase>("res.partner", partnerIds);

            Console.WriteLine(
                $"  var partners = env.GetRecords<IPartnerBase>(\"res.partner\", ids);"
            );
            Console.WriteLine($"  Processing {partners.Count} partners:");

            int companyCount = 0;
            foreach (var partner in partners)
            {
                if (partner.IsCompany)
                {
                    Console.WriteLine($"    - Company: {partner.Name}");
                    companyCount++;
                }
            }

            Console.WriteLine($"  Found {companyCount} companies");
        }

        private static void OptimizedBatchIterationDemo(OdooEnvironment env)
        {
            var partnerIds = Enumerable.Range(1, 10).Select(i => (RecordId)i).ToArray();

            Console.WriteLine($"  Processing {partnerIds.Length} partners (optimized batch way):");

            // Create batch context on the stack - uses unified schema
            var batch = new ResPartnerBatchContext(env.Columns, partnerIds);

            int companyCount = 0;
            for (int i = 0; i < partnerIds.Length; i++)
            {
                // Access using direct array indexing through batch context
                var name = batch.GetNameColumn()[i];
                var isCompany = batch.GetIsCompanyColumn()[i];

                if (isCompany)
                {
                    Console.WriteLine($"    - Company: {name}");
                    companyCount++;
                }
            }

            Console.WriteLine($"  Found {companyCount} companies");
            Console.WriteLine(
                "  Note: Column data loaded only once, all subsequent accesses are direct array indexing"
            );
        }

        private static void PerformanceComparisonDemo(OdooEnvironment env)
        {
            var partnerIds = Enumerable.Range(1, 1000).Select(i => (RecordId)i).ToArray();
            const int iterations = 100;

            Console.WriteLine(
                $"  Testing with {partnerIds.Length} partners, {iterations} iterations each\n"
            );

            // Test 1: RecordSet iteration using new API
            var sw = Stopwatch.StartNew();
            for (int iter = 0; iter < iterations; iter++)
            {
                var partners = env.GetRecords<IPartnerBase>("res.partner", partnerIds);
                int count = 0;
                foreach (var partner in partners)
                {
                    if (partner.IsCompany)
                    {
                        var _ = partner.Name; // Access property
                        count++;
                    }
                }
            }
            sw.Stop();
            var recordSetTime = sw.ElapsedMilliseconds;
            Console.WriteLine($"  RecordSet iteration: {recordSetTime}ms");

            // Test 2: Optimized batch iteration with BatchContext
            sw.Restart();
            for (int iter = 0; iter < iterations; iter++)
            {
                var batch = new ResPartnerBatchContext(env.Columns, partnerIds);
                int count = 0;

                var nameColumn = batch.GetNameColumn();
                var isCompanyColumn = batch.GetIsCompanyColumn();

                for (int i = 0; i < partnerIds.Length; i++)
                {
                    if (isCompanyColumn[i])
                    {
                        var _ = nameColumn[i];
                        count++;
                    }
                }
            }
            sw.Stop();
            var batchTime = sw.ElapsedMilliseconds;
            Console.WriteLine($"  Optimized batch iteration: {batchTime}ms");

            if (recordSetTime > 0)
            {
                var speedup = (double)recordSetTime / batchTime;
                Console.WriteLine($"\n  Performance improvement: {speedup:F1}x faster");
                Console.WriteLine(
                    $"  Time saved: {recordSetTime - batchTime}ms ({(1 - (double)batchTime / recordSetTime) * 100:F1}% reduction)"
                );
            }
        }

        /// <summary>
        /// Advanced example: Using batch context for complex calculations
        /// </summary>
        public static void AdvancedBatchCalculation(OdooEnvironment env, int[] partnerIds)
        {
            Console.WriteLine("\n=== Advanced Batch Calculation Example ===\n");

            // Use unified batch context
            var batchIds = partnerIds.Select(i => (RecordId)i).ToArray();
            var batch = new ResPartnerBatchContext(env.Columns, batchIds);

            // Pre-load all needed columns (triggers single batch fetch)
            var names = batch.GetNameColumn();
            var isCompanyFlags = batch.GetIsCompanyColumn();

            // Now perform calculations using direct array access
            decimal totalScore = 0;
            int companyCount = 0;

            for (int i = 0; i < partnerIds.Length; i++)
            {
                // Complex calculation using multiple fields
                if (isCompanyFlags[i])
                {
                    var score = names[i].Length * 1.5m; // Example scoring
                    totalScore += score;
                    companyCount++;
                }
            }

            Console.WriteLine($"Processed {partnerIds.Length} partners");
            Console.WriteLine($"Found {companyCount} companies");
            Console.WriteLine($"Total score: {totalScore:F2}");
            Console.WriteLine(
                "\nNote: All data accessed via direct array indexing - zero dictionary lookups!"
            );
        }
    }
}

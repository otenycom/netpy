using System;
using System.Diagnostics;
using System.Linq;
using Odoo.Core;
using Odoo.Models;
using Odoo.Models.Generated;
using Odoo.Generated;

namespace Odoo.Examples
{
    /// <summary>
    /// Demonstrates the new Data-Oriented Design optimizations with columnar storage.
    /// Shows both traditional and optimized batch access patterns.
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

            // Demo 1: Traditional single-record access (still works, backward compatible)
            Console.WriteLine("1. Traditional Single Record Access:");
            SingleRecordAccessDemo(env);
            Console.WriteLine();

            // Demo 2: Traditional iteration (works but less optimal)
            Console.WriteLine("2. Traditional Iteration:");
            TraditionalIterationDemo(env);
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
            var partnerIds = Enumerable.Range(1, 100).ToArray();
            
            // Load names
            var names = partnerIds.ToDictionary(
                id => id,
                id => $"Partner {id}"
            );
            env.Columns.BulkLoad(ModelSchema.Partner.ModelToken, ModelSchema.Partner.Name, names);

            // Load emails
            var emails = partnerIds.ToDictionary(
                id => id,
                id => $"partner{id}@example.com"
            );
            env.Columns.BulkLoad(ModelSchema.Partner.ModelToken, ModelSchema.Partner.Email, emails);

            // Load IsCompany flags
            var isCompanyFlags = partnerIds.ToDictionary(
                id => id,
                id => id % 3 == 0 // Every 3rd partner is a company
            );
            env.Columns.BulkLoad(ModelSchema.Partner.ModelToken, ModelSchema.Partner.IsCompany, isCompanyFlags);

            Console.WriteLine($"Loaded {partnerIds.Length} partners into columnar cache\n");
        }

        private static void SingleRecordAccessDemo(OdooEnvironment env)
        {
            // Access a single partner using the new optimized path
            var partner = env.Partner(1);
            
            Console.WriteLine($"  Partner ID: {partner.Id}");
            Console.WriteLine($"  Name: {partner.Name}");
            Console.WriteLine($"  Email: {partner.Email}");
            Console.WriteLine($"  Is Company: {partner.IsCompany}");
            
            // Writing still works
            partner.Name = "Updated Partner Name";
            Console.WriteLine($"  Updated Name: {partner.Name}");
        }

        private static void TraditionalIterationDemo(OdooEnvironment env)
        {
            var partnerIds = Enumerable.Range(1, 10).ToArray();
            var partners = env.Partners(partnerIds);

            Console.WriteLine($"  Processing {partners.Count} partners (traditional way):");
            
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
            var partnerIds = Enumerable.Range(1, 10).ToArray();

            Console.WriteLine($"  Processing {partnerIds.Length} partners (optimized batch way):");
            
            // Create batch context on the stack
            var batch = new PartnerBatchContext(env.Columns, partnerIds);

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
            Console.WriteLine("  Note: Column data loaded only once, all subsequent accesses are direct array indexing");
        }

        private static void PerformanceComparisonDemo(OdooEnvironment env)
        {
            var partnerIds = Enumerable.Range(1, 1000).ToArray();
            const int iterations = 100;

            Console.WriteLine($"  Testing with {partnerIds.Length} partners, {iterations} iterations each\n");

            // Test 1: Traditional iteration
            var sw = Stopwatch.StartNew();
            for (int iter = 0; iter < iterations; iter++)
            {
                var partners = env.Partners(partnerIds);
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
            var traditionalTime = sw.ElapsedMilliseconds;
            Console.WriteLine($"  Traditional iteration: {traditionalTime}ms");

            // Test 2: Optimized batch iteration
            sw.Restart();
            for (int iter = 0; iter < iterations; iter++)
            {
                var batch = new PartnerBatchContext(env.Columns, partnerIds);
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
            var optimizedTime = sw.ElapsedMilliseconds;
            Console.WriteLine($"  Optimized batch iteration: {optimizedTime}ms");

            if (traditionalTime > 0)
            {
                var speedup = (double)traditionalTime / optimizedTime;
                Console.WriteLine($"\n  Performance improvement: {speedup:F1}x faster");
                Console.WriteLine($"  Time saved: {traditionalTime - optimizedTime}ms ({(1 - (double)optimizedTime/traditionalTime)*100:F1}% reduction)");
            }
        }

        /// <summary>
        /// Advanced example: Using batch context for complex calculations
        /// </summary>
        public static void AdvancedBatchCalculation(OdooEnvironment env, int[] partnerIds)
        {
            Console.WriteLine("\n=== Advanced Batch Calculation Example ===\n");

            var batch = new PartnerBatchContext(env.Columns, partnerIds);
            
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
            Console.WriteLine("\nNote: All data accessed via direct array indexing - zero dictionary lookups!");
        }
    }
}
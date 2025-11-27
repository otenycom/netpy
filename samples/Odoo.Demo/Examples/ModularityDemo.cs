using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Odoo.Core;
using Odoo.Core.Modules;
using Odoo.Core.Pipeline;

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
                    var registrars = mod.Assembly.GetTypes()
                        .Where(t => typeof(IModuleRegistrar).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
                    
                    foreach (var registrarType in registrars)
                    {
                        var registrar = (IModuleRegistrar)Activator.CreateInstance(registrarType)!;
                        registrar.RegisterPipelines(pipelineRegistry);
                        registrar.RegisterFactories(modelRegistry);
                        Console.WriteLine($"   - Registered pipelines & factories from {mod.Manifest.Name}");
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
                Console.WriteLine($"     Contributing Interfaces: {model.ContributingInterfaces.Count}");
                foreach (var iface in model.ContributingInterfaces)
                {
                    Console.WriteLine($"       - {iface.FullName}");
                }
                Console.WriteLine($"     Fields: {model.Fields.Count}");
                foreach (var field in model.Fields.Values.OrderBy(f => f.FieldName))
                {
                    Console.WriteLine($"       - {field.FieldName}: {field.FieldType.Name} [Token: Field({field.Token.Token})] (from {field.ContributingInterface.Name})");
                }
            }

            // 6. Execute Pipeline Demo
            Console.WriteLine("\n6. Executing Pipeline: ActionVerify");
            Console.WriteLine("   Expected chain: Sale logic -> Base logic");
            
            try
            {
                // Find the IPartnerBase type from the loaded assemblies
                var partnerType = loadedModules
                    .Select(m => m.Assembly?.GetType("Odoo.Base.Models.IPartnerBase"))
                    .FirstOrDefault(t => t != null);

                if (partnerType == null)
                {
                    Console.WriteLine("   Error: Could not find IPartnerBase type.");
                    return;
                }

                // Get the record factory
                var factory = modelRegistry.GetRecordFactory("res.partner");
                
                // Create a test record with some data (Pythonic style!)
                // This uses the new ModelProxy and dynamic creation API
                var record = env["res.partner"].Create(new {
                    name = "Test Partner",
                    email = "test@example.com"
                });
                
                // Note: Strongly-typed Create API (e.g., env.Create(new PartnerBaseValues { ... }))
                // is available within addon modules that have the source generator referenced.
                // Since this demo dynamically loads modules, we use the dynamic API above.

                // Create the recordset using the generic CreateRecordSet method
                var genericCreateMethod = typeof(OdooEnvironment).GetMethod("CreateRecordSet")!.MakeGenericMethod(partnerType);
                var recordSet = genericCreateMethod.Invoke(env, new object[] { new[] { record.Id } });

                // Get and invoke the pipeline
                var pipeline = pipelineRegistry.GetPipeline<Delegate>("res.partner", "action_verify");
                Console.WriteLine($"   Pipeline delegate type: {pipeline.GetType().Name}");
                
                pipeline.DynamicInvoke(recordSet);
                Console.WriteLine("   ✓ Pipeline executed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   Pipeline execution: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"   Inner: {ex.InnerException.Message}");
            }

            Console.WriteLine("\n=== Demo Complete ===");
        }
    }
}
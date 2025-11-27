using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace Odoo.Core.Modules
{
    public class ModuleLoader
    {
        private readonly string _addonsPath;
        
        public ModuleLoader(string addonsPath)
        {
            _addonsPath = addonsPath;
        }

        public List<LoadedModule> LoadModules()
        {
            // 1. Discovery: Read all manifest.json files
            var availableModules = DiscoverModules();

            // 2. Resolution: Topological Sort based on 'Depends'
            var sortedManifests = ResolveDependencies(availableModules);

            // 3. Loading: Load assemblies in order
            var loadedModules = new List<LoadedModule>();
            
            foreach (var manifest in sortedManifests)
            {
                string moduleDir = Path.Combine(_addonsPath, manifest.Name);
                Assembly? asm = null;
                string? pythonPath = null;

                // Load C# Assembly if present
                if (!string.IsNullOrEmpty(manifest.AssemblyPath))
                {
                    string dllPath = Path.Combine(moduleDir, manifest.AssemblyPath);
                    if (File.Exists(dllPath))
                    {
                        try
                        {
                            // Use Default context for simplicity and to ensure shared types (like IOdooRecord) match
                            asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(dllPath);
                            Console.WriteLine($"[Loader] Assembly loaded: {asm.GetName().Name} with {asm.GetTypes().Length} types");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Loader] Error loading assembly {dllPath}: {ex.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[Loader] Warning: Assembly not found at {dllPath}");
                    }
                }

                // Resolve Python path if present
                if (!string.IsNullOrEmpty(manifest.PythonPath))
                {
                    pythonPath = Path.Combine(moduleDir, manifest.PythonPath);
                }

                var loadedModule = new LoadedModule(manifest, moduleDir, asm, pythonPath);
                loadedModules.Add(loadedModule);
                
                Console.WriteLine($"[Loader] Loaded Module: {manifest.Name} ({manifest.Version})");
            }

            return loadedModules;
        }

        private Dictionary<string, ModuleManifest> DiscoverModules()
        {
            var modules = new Dictionary<string, ModuleManifest>();
            
            if (!Directory.Exists(_addonsPath))
            {
                Console.WriteLine($"[Loader] Warning: Addons path '{_addonsPath}' does not exist.");
                return modules;
            }

            foreach (var dir in Directory.GetDirectories(_addonsPath))
            {
                string manifestPath = Path.Combine(dir, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    try 
                    {
                        var json = File.ReadAllText(manifestPath);
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var manifest = JsonSerializer.Deserialize<ModuleManifest>(json, options);
                        
                        if (manifest != null)
                        {
                            // Infer name from folder if not in JSON, or ensure it matches
                            var dirName = new DirectoryInfo(dir).Name;
                            if (string.IsNullOrEmpty(manifest.Name))
                            {
                                manifest = manifest with { Name = dirName };
                            }
                            
                            modules[manifest.Name] = manifest;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Loader] Error parsing manifest in {dir}: {ex.Message}");
                    }
                }
            }
            return modules;
        }

        private List<ModuleManifest> ResolveDependencies(Dictionary<string, ModuleManifest> modules)
        {
            var sorted = new List<ModuleManifest>();
            var visited = new HashSet<string>();
            var processing = new HashSet<string>();

            void Visit(string moduleName)
            {
                if (visited.Contains(moduleName)) return;
                if (processing.Contains(moduleName)) 
                    throw new Exception($"Circular dependency detected: {moduleName}");
                
                if (!modules.ContainsKey(moduleName))
                {
                    // In a real scenario, we might want to handle missing optional dependencies gracefully
                    // For now, we'll just log a warning and skip if it's a root dependency, 
                    // but if it's a required dependency of another module, that module will fail.
                    // Here we throw to be strict.
                    throw new Exception($"Missing dependency: {moduleName}");
                }

                processing.Add(moduleName);

                var manifest = modules[moduleName];
                foreach (var dep in manifest.Depends)
                {
                    Visit(dep);
                }

                processing.Remove(moduleName);
                visited.Add(moduleName);
                sorted.Add(manifest);
            }

            // We only want to load modules that are explicitly requested or are dependencies.
            // For this implementation, we'll load ALL discovered modules.
            // In a real app, you'd start with a list of "installed" modules.
            foreach (var mod in modules.Keys)
            {
                Visit(mod);
            }

            return sorted;
        }
    }
}
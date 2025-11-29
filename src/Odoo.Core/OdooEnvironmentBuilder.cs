using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Odoo.Core.Modules;
using Odoo.Core.Pipeline;

namespace Odoo.Core
{
    /// <summary>
    /// Builder for creating fully configured OdooEnvironment instances.
    /// Auto-discovers IModuleRegistrar implementations from loaded assemblies.
    ///
    /// <para>
    /// <b>Usage:</b>
    /// <code>
    /// var env = new OdooEnvironmentBuilder()
    ///     .WithUserId(1)
    ///     .Build();
    /// </code>
    /// </para>
    ///
    /// <para>
    /// <b>Static Compilation Architecture:</b>
    /// This builder works with NetPy's static compilation approach where all addons
    /// are compiled together into the final application. The builder discovers
    /// IModuleRegistrar implementations from loaded assemblies and registers
    /// their pipelines and factories in dependency order.
    /// </para>
    /// </summary>
    public class OdooEnvironmentBuilder
    {
        private int _userId = 1;
        private IColumnarCache? _cache;
        private readonly List<Assembly> _additionalAssemblies = new();
        private readonly List<IModuleRegistrar> _additionalRegistrars = new();

        /// <summary>
        /// Set the user ID for the environment.
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <returns>This builder for chaining</returns>
        public OdooEnvironmentBuilder WithUserId(int userId)
        {
            _userId = userId;
            return this;
        }

        /// <summary>
        /// Use a custom cache implementation.
        /// </summary>
        /// <param name="cache">The cache to use</param>
        /// <returns>This builder for chaining</returns>
        public OdooEnvironmentBuilder WithCache(IColumnarCache cache)
        {
            _cache = cache;
            return this;
        }

        /// <summary>
        /// Add an additional assembly to scan (beyond auto-discovered ones).
        /// Useful for testing or special scenarios where assemblies aren't
        /// automatically discovered.
        /// </summary>
        /// <param name="assembly">The assembly to add</param>
        /// <returns>This builder for chaining</returns>
        public OdooEnvironmentBuilder AddAssembly(Assembly assembly)
        {
            _additionalAssemblies.Add(assembly);
            return this;
        }

        /// <summary>
        /// Add a specific registrar to be invoked.
        /// Useful for testing or when registrars need to be added explicitly.
        /// </summary>
        /// <param name="registrar">The registrar to add</param>
        /// <returns>This builder for chaining</returns>
        public OdooEnvironmentBuilder AddRegistrar(IModuleRegistrar registrar)
        {
            _additionalRegistrars.Add(registrar);
            return this;
        }

        /// <summary>
        /// Build the configured OdooEnvironment.
        ///
        /// <para>
        /// This method:
        /// <list type="number">
        /// <item>Discovers all assemblies referencing Odoo.Core</item>
        /// <item>Finds IModuleRegistrar implementations</item>
        /// <item>Scans for [OdooModel] interfaces</item>
        /// <item>Registers pipelines and factories</item>
        /// <item>Compiles pipelines</item>
        /// <item>Returns configured environment</item>
        /// </list>
        /// </para>
        /// </summary>
        /// <returns>A fully configured OdooEnvironment</returns>
        public OdooEnvironment Build()
        {
            // 1. Discover addon assemblies
            var addonAssemblies = DiscoverAddonAssemblies();

            // 2. Create registries
            var pipelineRegistry = new PipelineRegistry();
            var registryBuilder = new RegistryBuilder();

            // 3. Scan assemblies for models
            foreach (var assembly in addonAssemblies)
            {
                registryBuilder.ScanAssembly(assembly);
            }
            var modelRegistry = registryBuilder.Build();

            // 4. Discover and invoke registrars
            var registrars = DiscoverRegistrars(addonAssemblies);

            // Add any explicitly added registrars
            registrars.AddRange(_additionalRegistrars);

            foreach (var registrar in registrars)
            {
                registrar.RegisterPipelines(pipelineRegistry);
            }

            // Register factories - the last registrar typically has the unified wrappers
            // which see all extensions and create the complete record types
            if (registrars.Count > 0)
            {
                registrars.Last().RegisterFactories(modelRegistry);
            }

            // 5. Compile pipelines
            pipelineRegistry.CompileAll();

            // 6. Create environment
            return new OdooEnvironment(_userId, _cache, modelRegistry, pipelineRegistry);
        }

        /// <summary>
        /// Discover all assemblies that are potential addon assemblies.
        /// These are assemblies that reference Odoo.Core.
        /// </summary>
        private List<Assembly> DiscoverAddonAssemblies()
        {
            var coreAssembly = typeof(OdooEnvironment).Assembly;
            var coreAssemblyName = coreAssembly.GetName().Name;

            var addonAssemblies = new List<Assembly>();

            // Get all loaded assemblies
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in loadedAssemblies)
            {
                try
                {
                    // Skip dynamic assemblies
                    if (assembly.IsDynamic)
                        continue;

                    var name = assembly.GetName().Name;
                    if (name == null)
                        continue;

                    // Skip system assemblies
                    if (
                        name.StartsWith("System", StringComparison.Ordinal)
                        || name.StartsWith("Microsoft", StringComparison.Ordinal)
                        || name.StartsWith("netstandard", StringComparison.Ordinal)
                        || name.StartsWith("mscorlib", StringComparison.Ordinal)
                    )
                        continue;

                    // Check if this assembly references Odoo.Core
                    var references = assembly.GetReferencedAssemblies();
                    if (references.Any(r => r.Name == coreAssemblyName))
                    {
                        addonAssemblies.Add(assembly);
                    }
                }
                catch (Exception)
                {
                    // Skip assemblies that can't be inspected
                }
            }

            // Add any explicitly added assemblies
            foreach (var assembly in _additionalAssemblies)
            {
                if (!addonAssemblies.Contains(assembly))
                {
                    addonAssemblies.Add(assembly);
                }
            }

            return addonAssemblies;
        }

        /// <summary>
        /// Find all IModuleRegistrar implementations in the given assemblies.
        /// Returns them sorted by dependency order (base modules first).
        /// </summary>
        private List<IModuleRegistrar> DiscoverRegistrars(List<Assembly> assemblies)
        {
            var registrars = new List<(IModuleRegistrar Registrar, Assembly Assembly)>();

            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes();
                    var registrarTypes = types.Where(t =>
                        !t.IsAbstract
                        && !t.IsInterface
                        && typeof(IModuleRegistrar).IsAssignableFrom(t)
                    );

                    foreach (var type in registrarTypes)
                    {
                        try
                        {
                            var registrar = (IModuleRegistrar)Activator.CreateInstance(type)!;
                            registrars.Add((registrar, assembly));
                        }
                        catch (Exception ex)
                        {
                            // Log but continue
                            Console.WriteLine(
                                $"[OdooEnvironmentBuilder] Failed to instantiate registrar {type.Name}: {ex.Message}"
                            );
                        }
                    }
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // Handle assembly load issues - process types that loaded successfully
                    var loadedTypes = ex.Types.Where(t => t != null);
                    var registrarTypes = loadedTypes.Where(t =>
                        !t!.IsAbstract
                        && !t.IsInterface
                        && typeof(IModuleRegistrar).IsAssignableFrom(t)
                    );

                    foreach (var type in registrarTypes)
                    {
                        try
                        {
                            var registrar = (IModuleRegistrar)Activator.CreateInstance(type!)!;
                            registrars.Add((registrar, assembly));
                        }
                        catch
                        {
                            // Skip this registrar
                        }
                    }
                }
                catch (Exception)
                {
                    // Skip this assembly
                }
            }

            // Sort by dependency order
            return SortByDependencyOrder(registrars);
        }

        /// <summary>
        /// Sort registrars by dependency order.
        /// Assemblies that are referenced by others come first.
        /// </summary>
        private List<IModuleRegistrar> SortByDependencyOrder(
            List<(IModuleRegistrar Registrar, Assembly Assembly)> registrars
        )
        {
            var assemblies = registrars.Select(r => r.Assembly).Distinct().ToList();

            // Build a dependency order map
            // Assemblies with fewer dependencies on other addon assemblies come first
            var dependencyCount = new Dictionary<Assembly, int>();

            foreach (var assembly in assemblies)
            {
                var referencedNames = assembly.GetReferencedAssemblies().Select(r => r.Name);
                var count = assemblies.Count(other =>
                    other != assembly && referencedNames.Contains(other.GetName().Name)
                );
                dependencyCount[assembly] = count;
            }

            // Sort: fewer dependencies = earlier in order
            return registrars
                .OrderBy(r => dependencyCount.GetValueOrDefault(r.Assembly, 0))
                .Select(r => r.Registrar)
                .ToList();
        }
    }
}

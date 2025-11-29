using System.Reflection;
using System.Runtime.Loader;

namespace Odoo.Core.Modules
{
    public class ModuleLoadContext : AssemblyLoadContext
    {
        private readonly string _modulePath;

        public ModuleLoadContext(string modulePath)
            : base(isCollectible: true)
        {
            _modulePath = modulePath;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Try to load from the module directory first
            string assemblyPath = Path.Combine(_modulePath, $"{assemblyName.Name}.dll");
            if (File.Exists(assemblyPath))
            {
                return LoadFromAssemblyPath(assemblyPath);
            }

            // Fallback to default context
            return null;
        }
    }
}

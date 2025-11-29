using System.Reflection;

namespace Odoo.Core.Modules
{
    public class LoadedModule
    {
        public ModuleManifest Manifest { get; }
        public Assembly? Assembly { get; }
        public string? PythonModulePath { get; }
        public string DirectoryPath { get; }

        public LoadedModule(
            ModuleManifest manifest,
            string directoryPath,
            Assembly? assembly = null,
            string? pythonModulePath = null
        )
        {
            Manifest = manifest;
            DirectoryPath = directoryPath;
            Assembly = assembly;
            PythonModulePath = pythonModulePath;
        }
    }
}

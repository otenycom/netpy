using System;

namespace Odoo.Core.Modules
{
    public record ModuleManifest
    {
        public string Name { get; init; } = "";
        public string Version { get; init; } = "1.0.0";
        public string[] Depends { get; init; } = Array.Empty<string>();
        public string[] AutoInstall { get; init; } = Array.Empty<string>();
        public string? AssemblyPath { get; init; } // Relative: bin/Module.dll
        public string? PythonPath { get; init; } // Relative: python/
        public string Description { get; init; } = "";
        public string Author { get; init; } = "";
        public string Category { get; init; } = "";
        public bool Installable { get; init; } = true;
        public bool Application { get; init; } = false;
    }
}

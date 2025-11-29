# NetPy Sidecar Tool Architecture

## Overview

The **NetPy Sidecar** is a CLI tool that manages addon installation and removal for NetPy applications. It enables dynamic addon management while maintaining the static compilation architecture that provides type safety and optimal performance.

The sidecar operates by:
1. Backing up the target `.csproj` file
2. Modifying project references to add/remove addon
3. Building the solution to validate changes
4. On success: keeping the modified csproj
5. On failure: restoring the backup and reporting errors

The user then restarts their app manually using `dotnet run` to load the new addon configuration.

## Design Principles

| Principle | Description |
|-----------|-------------|
| **Simple & Safe** | Backup csproj before modification; restore on build failure |
| **Atomic Operations** | Either the addon is fully installed/removed, or no changes are made |
| **User-Controlled Restart** | User decides when to restart the app after addon changes |
| **Static Compilation** | Maintains compile-time type safety - no dynamic assembly loading |
| **Project-Centric** | Works with any project that references Odoo addons |

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         SIDECAR CLI WORKFLOW                             │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  User Command                                                            │
│  ────────────                                                            │
│  $ netpy addon install purchase --project samples/Odoo.Demo             │
│       │                                                                  │
│       ▼                                                                  │
│  ┌──────────────────┐                                                    │
│  │ 1. DISCOVER      │  Parse csproj for installed addons                │
│  │    ADDONS        │  Scan addons/ folder for available addons         │
│  └────────┬─────────┘                                                    │
│           │                                                              │
│           ▼                                                              │
│  ┌──────────────────┐                                                    │
│  │ 2. VALIDATE      │  Check addon exists                               │
│  │    REQUEST       │  Check dependencies are installed                 │
│  │                  │  Check for conflicts                              │
│  └────────┬─────────┘                                                    │
│           │                                                              │
│           ▼                                                              │
│  ┌──────────────────┐                                                    │
│  │ 3. BACKUP        │  Copy target.csproj → target.csproj.backup        │
│  │    CSPROJ        │                                                   │
│  └────────┬─────────┘                                                    │
│           │                                                              │
│           ▼                                                              │
│  ┌──────────────────┐                                                    │
│  │ 4. MODIFY CSPROJ │  Add/remove ProjectReference                      │
│  │                  │  Update ItemGroup                                 │
│  └────────┬─────────┘                                                    │
│           │                                                              │
│           ▼                                                              │
│  ┌──────────────────┐                                                    │
│  │ 5. BUILD         │  dotnet build                                     │
│  │    SOLUTION      │  Capture build output                             │
│  └────────┬─────────┘                                                    │
│           │                                                              │
│      ┌────┴────┐                                                         │
│      │ Success │                                                         │
│      │   ?     │                                                         │
│      └────┬────┘                                                         │
│           │                                                              │
│     ┌─────┴─────┐                                                        │
│     ▼           ▼                                                        │
│  ┌──────┐    ┌──────┐                                                    │
│  │ YES  │    │  NO  │                                                    │
│  └──┬───┘    └──┬───┘                                                    │
│     │           │                                                        │
│     ▼           ▼                                                        │
│  ┌──────────────────┐    ┌──────────────────┐                            │
│  │ 6a. CLEANUP      │    │ 6b. RESTORE      │                            │
│  │     Remove       │    │     Restore from │                            │
│  │     backup file  │    │     backup file  │                            │
│  │     Print        │    │     Print errors │                            │
│  │     success msg  │    │                  │                            │
│  └────────┬─────────┘    └────────┬─────────┘                            │
│           │                       │                                      │
│           └───────────┬───────────┘                                      │
│                       ▼                                                  │
│              ┌──────────────────┐                                        │
│              │ 7. DONE          │  User runs: dotnet run                │
│              │    (manual)      │  to restart with new addons           │
│              └──────────────────┘                                        │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

## CLI Interface

### Commands

```bash
# Install an addon
netpy addon install <addon-name> [options]

# Remove an addon
netpy addon remove <addon-name> [options]

# List addons
netpy addon list [options]

# Show addon info
netpy addon info <addon-name>
```

### Options

| Option | Description | Default |
|--------|-------------|---------|
| `--project <path>` | Path to target .csproj file | Auto-detect from current directory |
| `--solution <path>` | Path to solution file | Auto-detect `*.slnx` or `*.sln` |
| `--addons-path <path>` | Path to addons folder | `./addons` |
| `--verbose` | Show detailed output | `false` |
| `--dry-run` | Show what would happen without making changes | `false` |

### Examples

```bash
# Install purchase addon to the demo project
netpy addon install purchase --project samples/Odoo.Demo/Odoo.Demo.csproj

# Remove purchase addon
netpy addon remove purchase --project samples/Odoo.Demo/Odoo.Demo.csproj

# List all addons (installed and available)
netpy addon list --project samples/Odoo.Demo/Odoo.Demo.csproj

# Dry run to see what would happen
netpy addon install purchase --project samples/Odoo.Demo --dry-run

# After successful install, restart the app manually
cd samples/Odoo.Demo && dotnet run
```

## Addon Discovery

### Installed Addons Detection

Installed addons are detected by parsing the target `.csproj` file for `<ProjectReference>` elements pointing to addon projects:

```csharp
// Parse csproj for installed addons
public List<InstalledAddon> GetInstalledAddons(string csprojPath)
{
    var doc = XDocument.Load(csprojPath);
    var references = doc.Descendants("ProjectReference")
        .Select(e => e.Attribute("Include")?.Value)
        .Where(path => path != null && IsAddonProject(path))
        .Select(path => ParseAddonFromPath(path))
        .ToList();
    return references;
}
```

Example csproj showing installed addons:

```xml
<ItemGroup>
  <ProjectReference Include="../../src/Odoo.Core/Odoo.Core.csproj" />
  <ProjectReference Include="../../addons/base/Odoo.Base.csproj" />   <!-- Installed -->
  <ProjectReference Include="../../addons/sale/Odoo.Sale.csproj" />   <!-- Installed -->
  <!-- purchase is NOT installed - no reference here -->
</ItemGroup>
```

### Available Addons Discovery

Available addons are discovered by scanning the addons folder for directories containing a `manifest.json`:

```csharp
// Scan addons folder for available addons
public List<AvailableAddon> ScanAvailableAddons(string addonsPath)
{
    var addons = new List<AvailableAddon>();
    
    foreach (var dir in Directory.GetDirectories(addonsPath))
    {
        var manifestPath = Path.Combine(dir, "manifest.json");
        if (File.Exists(manifestPath))
        {
            var manifest = JsonSerializer.Deserialize<AddonManifest>(
                File.ReadAllText(manifestPath));
            addons.Add(new AvailableAddon
            {
                Name = manifest.Name,
                Path = dir,
                Manifest = manifest
            });
        }
    }
    
    return addons;
}
```

### Manifest Schema

```json
{
  "name": "purchase",
  "version": "1.0.0",
  "depends": ["base"],
  "description": "Purchase management module",
  "assemblyPath": "bin/Debug/net10.0/Odoo.Purchase.dll"
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `name` | Yes | Unique addon identifier |
| `version` | Yes | Semantic version |
| `depends` | No | List of addon names this addon depends on |
| `description` | No | Human-readable description |
| `assemblyPath` | No | Relative path to built assembly (for validation) |

## Dependency Resolution

When installing an addon, the sidecar must ensure all dependencies are installed:

```
┌─────────────────────────────────────────────────────────────────┐
│                   DEPENDENCY RESOLUTION                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Request: Install "purchase"                                     │
│                                                                  │
│  1. Load manifest for "purchase"                                 │
│     depends: ["base"]                                            │
│                                                                  │
│  2. Check installed addons in target csproj                      │
│     - base: ✓ installed                                          │
│     - sale: ✓ installed (not required, ignore)                   │
│                                                                  │
│  3. All dependencies satisfied → Proceed with install            │
│                                                                  │
│  If "base" was NOT installed:                                    │
│  - Option A: Auto-install dependencies (with --auto-deps flag)   │
│  - Option B: Error with message listing missing dependencies     │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

### Dependency Errors

```bash
$ netpy addon install purchase --project samples/Odoo.Demo

ERROR: Cannot install 'purchase'. Missing dependencies:
  - base (required by: purchase)

Run with --auto-deps to automatically install dependencies:
  netpy addon install purchase --project samples/Odoo.Demo --auto-deps
```

## Project Reference Modification

### Adding a ProjectReference

```xml
<!-- Before -->
<ItemGroup>
  <ProjectReference Include="../../src/Odoo.Core/Odoo.Core.csproj" />
  <ProjectReference Include="../../addons/base/Odoo.Base.csproj" />
  <ProjectReference Include="../../addons/sale/Odoo.Sale.csproj" />
</ItemGroup>

<!-- After: netpy addon install purchase -->
<ItemGroup>
  <ProjectReference Include="../../src/Odoo.Core/Odoo.Core.csproj" />
  <ProjectReference Include="../../addons/base/Odoo.Base.csproj" />
  <ProjectReference Include="../../addons/sale/Odoo.Sale.csproj" />
  <ProjectReference Include="../../addons/purchase/Odoo.Purchase.csproj" />
</ItemGroup>
```

### Removing a ProjectReference

```xml
<!-- Before -->
<ItemGroup>
  <ProjectReference Include="../../src/Odoo.Core/Odoo.Core.csproj" />
  <ProjectReference Include="../../addons/base/Odoo.Base.csproj" />
  <ProjectReference Include="../../addons/sale/Odoo.Sale.csproj" />
  <ProjectReference Include="../../addons/purchase/Odoo.Purchase.csproj" />
</ItemGroup>

<!-- After: netpy addon remove purchase -->
<ItemGroup>
  <ProjectReference Include="../../src/Odoo.Core/Odoo.Core.csproj" />
  <ProjectReference Include="../../addons/base/Odoo.Base.csproj" />
  <ProjectReference Include="../../addons/sale/Odoo.Sale.csproj" />
</ItemGroup>
```

### Path Resolution

The sidecar calculates relative paths from the target csproj to the addon csproj:

```csharp
public string GetRelativeProjectReference(string targetCsproj, string addonCsproj)
{
    var targetDir = Path.GetDirectoryName(Path.GetFullPath(targetCsproj));
    var addonPath = Path.GetFullPath(addonCsproj);
    return Path.GetRelativePath(targetDir, addonPath);
}

// Example:
// targetCsproj: samples/Odoo.Demo/Odoo.Demo.csproj
// addonCsproj:  addons/purchase/Odoo.Purchase.csproj
// Result:       ../../addons/purchase/Odoo.Purchase.csproj
```

## Backup Strategy

### Simple Backup Approach

Instead of copying the entire solution, we only backup the csproj file being modified:

```csharp
public class CsprojBackupManager
{
    public string CreateBackup(string csprojPath)
    {
        var backupPath = csprojPath + ".backup";
        File.Copy(csprojPath, backupPath, overwrite: true);
        return backupPath;
    }
    
    public void RestoreBackup(string csprojPath)
    {
        var backupPath = csprojPath + ".backup";
        if (File.Exists(backupPath))
        {
            File.Copy(backupPath, csprojPath, overwrite: true);
            File.Delete(backupPath);
        }
    }
    
    public void CleanupBackup(string csprojPath)
    {
        var backupPath = csprojPath + ".backup";
        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }
    }
}
```

### Backup Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                     BACKUP & RESTORE FLOW                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Before modification:                                            │
│  ┌─────────────────────────────────────┐                         │
│  │ Odoo.Demo.csproj                    │                         │
│  │   → Copy to Odoo.Demo.csproj.backup │                         │
│  └─────────────────────────────────────┘                         │
│                                                                  │
│  On BUILD SUCCESS:                                               │
│  ┌─────────────────────────────────────┐                         │
│  │ Delete Odoo.Demo.csproj.backup      │                         │
│  │ Keep modified Odoo.Demo.csproj      │                         │
│  └─────────────────────────────────────┘                         │
│                                                                  │
│  On BUILD FAILURE:                                               │
│  ┌─────────────────────────────────────┐                         │
│  │ Restore Odoo.Demo.csproj.backup     │                         │
│  │   → Odoo.Demo.csproj                │                         │
│  │ Delete backup file                  │                         │
│  └─────────────────────────────────────┘                         │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

## User Workflow

The sidecar is designed for a simple CLI-driven workflow where the user manages app restarts:

```bash
# 1. Stop your running app (Ctrl+C in the terminal running dotnet run)

# 2. Install an addon
$ netpy addon install purchase --project samples/Odoo.Demo/Odoo.Demo.csproj

✓ Addon 'purchase' installed successfully
  Modified: samples/Odoo.Demo/Odoo.Demo.csproj

To apply changes, restart your app:
  cd samples/Odoo.Demo && dotnet run

# 3. Restart your app
$ cd samples/Odoo.Demo && dotnet run
```

### Future Enhancement: App-Integrated Sidecar

In a future phase, the sidecar could be integrated directly into the running app, allowing addon installation without manual restart:

```
┌─────────────────────────────────────────────────────────────────┐
│                 FUTURE: APP-INTEGRATED SIDECAR                   │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Running App                                                     │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │                                                            │  │
│  │  ┌──────────────┐      ┌─────────────────────────────┐    │  │
│  │  │ HTTP API     │ ───▶ │ Embedded Sidecar            │    │  │
│  │  │ /addons/...  │      │ - Modify csproj             │    │  │
│  │  └──────────────┘      │ - Build solution            │    │  │
│  │                        │ - Signal app to restart     │    │  │
│  │                        └─────────────────────────────┘    │  │
│  │                                     │                      │  │
│  │                                     ▼                      │  │
│  │                        ┌─────────────────────────────┐    │  │
│  │                        │ App auto-restarts with new  │    │  │
│  │                        │ addon configuration         │    │  │
│  │                        └─────────────────────────────┘    │  │
│  │                                                            │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                  │
│  This is OUT OF SCOPE for initial implementation.               │
│  Document here for future reference.                             │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

## Project Detection

### Auto-Detection Strategy

When `--project` is not specified, the sidecar attempts to detect the target project:

```csharp
public string DetectTargetProject(string workingDirectory)
{
    // Strategy 1: Look for .csproj in current directory
    var localCsproj = Directory.GetFiles(workingDirectory, "*.csproj")
        .FirstOrDefault();
    if (localCsproj != null && HasMainEntryPoint(localCsproj))
        return localCsproj;
    
    // Strategy 2: Look for solution file and find executable projects
    var solution = FindSolutionFile(workingDirectory);
    if (solution != null)
    {
        var executableProjects = GetExecutableProjects(solution);
        if (executableProjects.Count == 1)
            return executableProjects[0];
        
        if (executableProjects.Count > 1)
            throw new Exception(
                "Multiple executable projects found. " +
                "Please specify --project <path>");
    }
    
    throw new Exception(
        "Could not detect target project. " +
        "Please specify --project <path>");
}

private bool HasMainEntryPoint(string csprojPath)
{
    var doc = XDocument.Load(csprojPath);
    var outputType = doc.Descendants("OutputType").FirstOrDefault()?.Value;
    return outputType == "Exe";
}
```

## Error Handling

### Build Failure

```
$ netpy addon install purchase --project samples/Odoo.Demo

[1/5] Discovering addons...              ✓
[2/5] Validating request...              ✓
[3/5] Backing up csproj...               ✓
[4/5] Modifying project references...    ✓
[5/5] Building solution...               ✗

BUILD FAILED

Error CS0246: The type or namespace name 'IPartnerPurchaseExtension' could not
be found (are you missing a using directive or an assembly reference?)

Rolling back changes...
  Restoring csproj from backup...        ✓

✗ Addon installation failed. No changes were made to your project.
  Check the build errors above and try again.
```

### Missing Dependency

```
$ netpy addon install purchase --project samples/Odoo.Demo

ERROR: Cannot install 'purchase'. Missing dependencies:
  - base (required by: purchase)

Hint: Install missing dependencies first:
  netpy addon install base --project samples/Odoo.Demo
```

### Addon Not Found

```
$ netpy addon install nonexistent --project samples/Odoo.Demo

ERROR: Addon 'nonexistent' not found.

Available addons:
  - base (installed)
  - sale (installed)
  - purchase (available)

Searched in: ./addons
```

## Future Enhancements

### Prebuilt DLL Support (Phase 2)

The architecture is designed to support prebuilt DLL addons in a future phase:

```xml
<!-- Future: Adding prebuilt DLL as Reference instead of ProjectReference -->
<ItemGroup>
  <Reference Include="Odoo.ExternalAddon">
    <HintPath>../../addons/external/Odoo.ExternalAddon.dll</HintPath>
  </Reference>
</ItemGroup>
```

Requirements for prebuilt DLL support:
- Manifest.json must exist alongside DLL with metadata
- DLL must be built against compatible Odoo.Core version
- Source generator output must be included in the DLL
- Dependencies must be resolvable

### NuGet Package Addons (Phase 3)

```xml
<!-- Future: Addons as NuGet packages -->
<ItemGroup>
  <PackageReference Include="Odoo.Addon.Accounting" Version="1.0.0" />
</ItemGroup>
```

## Implementation Structure

```
src/
└── Odoo.Sidecar/
    ├── Odoo.Sidecar.csproj
    ├── Program.cs                    # CLI entry point
    ├── Commands/
    │   ├── InstallCommand.cs         # netpy addon install
    │   ├── RemoveCommand.cs          # netpy addon remove
    │   ├── ListCommand.cs            # netpy addon list
    │   └── InfoCommand.cs            # netpy addon info
    ├── Services/
    │   ├── AddonDiscoveryService.cs  # Scan for addons
    │   ├── DependencyResolver.cs     # Resolve dependencies
    │   ├── ProjectModifier.cs        # Modify csproj files
    │   ├── CsprojBackupService.cs    # Backup and restore csproj
    │   └── BuildService.cs           # Run dotnet build
    ├── Models/
    │   ├── AddonManifest.cs          # Manifest.json model
    │   ├── InstalledAddon.cs         # Installed addon info
    │   └── AvailableAddon.cs         # Available addon info
    └── Utils/
        ├── PathResolver.cs           # Path calculations
        └── ConsoleOutput.cs          # Formatted output
```

## Test Flow: Purchase Addon

This section describes the exact test flow using the `purchase` addon with `Odoo.Demo`.

### Initial State

`samples/Odoo.Demo/Odoo.Demo.csproj` has base and sale installed:
```xml
<ItemGroup>
  <ProjectReference Include="../../addons/base/Odoo.Base.csproj" />
  <ProjectReference Include="../../addons/sale/Odoo.Sale.csproj" />
</ItemGroup>
```

### Test 1: List Addons

```bash
$ netpy addon list --project samples/Odoo.Demo/Odoo.Demo.csproj

Installed addons:
  ✓ base      v1.0.0  - Base module containing core models
  ✓ sale      v1.0.0  - Sales management module

Available addons:
  ○ purchase  v1.0.0  - Purchase management module
```

### Test 2: Install Purchase Addon

```bash
$ netpy addon install purchase --project samples/Odoo.Demo/Odoo.Demo.csproj

Installing addon 'purchase'...
  Checking dependencies... ✓ (requires: base)
  Backing up Odoo.Demo.csproj... ✓
  Adding project reference... ✓
  Building solution... ✓

✓ Addon 'purchase' installed successfully!

To apply changes, restart your app:
  cd samples/Odoo.Demo && dotnet run
```

### Test 3: Verify Installation

```bash
$ netpy addon list --project samples/Odoo.Demo/Odoo.Demo.csproj

Installed addons:
  ✓ base      v1.0.0  - Base module containing core models
  ✓ sale      v1.0.0  - Sales management module
  ✓ purchase  v1.0.0  - Purchase management module

Available addons:
  (all addons installed)
```

### Test 4: Remove Purchase Addon

```bash
$ netpy addon remove purchase --project samples/Odoo.Demo/Odoo.Demo.csproj

Removing addon 'purchase'...
  Checking dependents... ✓ (no addons depend on purchase)
  Backing up Odoo.Demo.csproj... ✓
  Removing project reference... ✓
  Building solution... ✓

✓ Addon 'purchase' removed successfully!

To apply changes, restart your app:
  cd samples/Odoo.Demo && dotnet run
```

### Test 5: Dry Run

```bash
$ netpy addon install purchase --project samples/Odoo.Demo/Odoo.Demo.csproj --dry-run

DRY RUN - No changes will be made

Would install addon 'purchase':
  Dependencies: base ✓ (installed)
  Would add to Odoo.Demo.csproj:
    <ProjectReference Include="../../addons/purchase/Odoo.Purchase.csproj" />

Run without --dry-run to apply changes.
```

### Test 6: Build Failure Rollback

```bash
# Simulate by temporarily breaking purchase addon code
$ netpy addon install purchase --project samples/Odoo.Demo/Odoo.Demo.csproj

Installing addon 'purchase'...
  Checking dependencies... ✓ (requires: base)
  Backing up Odoo.Demo.csproj... ✓
  Adding project reference... ✓
  Building solution... ✗

BUILD FAILED:
  error CS0246: The type or namespace name 'SomeType' could not be found

Rolling back changes...
  Restoring Odoo.Demo.csproj from backup... ✓

✗ Addon installation failed. No changes were made.
```

## Testing Strategy

### Integration Test Example

```csharp
[Fact]
public async Task InstallAddon_Success_AddsProjectReference()
{
    // Arrange
    var tempSolution = CreateTempSolutionCopy();
    var targetProject = Path.Combine(tempSolution, "samples/Odoo.Demo/Odoo.Demo.csproj");
    
    // Act
    var result = await Sidecar.InstallAddonAsync("purchase", targetProject);
    
    // Assert
    Assert.True(result.Success);
    Assert.Contains("purchase", GetInstalledAddons(targetProject));
    
    // Verify build works
    var buildResult = await DotNetBuild(targetProject);
    Assert.True(buildResult.Success);
}
```

## Configuration

### Optional Configuration File

`netpy.config.json` in solution root:

```json
{
  "addonsPath": "./addons",
  "defaultProject": "samples/Odoo.Demo/Odoo.Demo.csproj",
  "buildConfiguration": "Debug"
}
```

## Summary

The NetPy Sidecar tool provides a simple, safe way to manage addon installation and removal while maintaining the static compilation architecture. Key features:

- **CLI-based**: Simple commands for addon management (`netpy addon install/remove/list`)
- **Safe operations**: Backup csproj before modification, restore on build failure
- **User-controlled restart**: User decides when to restart app with `dotnet run`
- **Dependency-aware**: Validates dependencies before installation
- **Project-centric**: Works with any project that references Odoo addons

The design prioritizes simplicity and safety, ensuring that addon operations never leave the project in a broken state. Future enhancements may include app-integrated sidecar with automatic restart capabilities.
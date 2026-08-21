# PCL3

PCL3 is an experimental next-generation architecture for Plain Craft Launcher.

The goal is not to rewrite PCL2 line-by-line. PCL3 is being built as a cross-platform Minecraft launcher core with a desktop UI on top.

## Current bootstrap

The first architecture slice contains:

- .NET 10 / C# 14;
- Avalonia 12 desktop shell;
- explicit Windows / macOS / Linux and CPU-architecture modeling;
- Mojang launcher rule evaluation;
- Java runtime compatibility analysis;
- unit tests;
- CI on Windows, macOS and Linux.

## Architecture constraints

- Minecraft/business logic must not depend on the UI.
- OS-specific behavior must not leak throughout the core.
- Managed C# is the required baseline implementation.
- Native Rust/C/C++ is optional acceleration only.
- Compatibility between Minecraft, Java, loaders, mods, OS and architecture is modeled explicitly.

See [docs/architecture.md](docs/architecture.md).

## Build

Requires the .NET 10 SDK.

```bash
dotnet restore PCL3.slnx
dotnet build PCL3.slnx
dotnet test tests/PCL.Minecraft.Tests/PCL.Minecraft.Tests.csproj
dotnet run --project src/PCL.Desktop/PCL.Desktop.csproj
```

## Near-term roadmap

1. Extract a platform-neutral Minecraft version/metadata model.
2. Build a deterministic `LaunchPlan` generator with golden tests.
3. Add Java runtime discovery/resolution behind platform services.
4. Introduce resource indexing and Modrinth/CurseForge models.
5. Add a compatibility scanner for native libraries inside mods.
6. Benchmark managed resource scanning before adding any native accelerator.

PCL2 remains the reference for behavior; PCL3 should migrate semantics, not historical implementation accidents.

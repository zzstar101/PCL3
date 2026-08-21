# PCL3 architecture

PCL3 is a cross-platform Minecraft launcher architecture, not a direct WPF-to-Avalonia port.

## Principles

1. **Minecraft-first** — launcher behavior is modeled independently of UI.
2. **Platform-aware core** — operating system and CPU architecture are explicit data.
3. **Platform-independent business logic** — Minecraft, resources, networking and instance logic must not depend on Avalonia or OS-specific APIs.
4. **Managed-first** — every required feature has a managed C# implementation.
5. **Native-optional** — Rust/C/C++ may accelerate measured hot paths, but failure to load native code must never prevent the launcher from working.
6. **No scattered OS branches** — platform-specific behavior belongs behind platform services.
7. **Compatibility is data** — Minecraft version, Java, loader, mod natives, OS and CPU architecture are evaluated by a compatibility engine rather than ad-hoc checks.

## Initial dependency direction

```text
PCL.Desktop
    |
    +--> PCL.Minecraft --> PCL.Core
    |          |
    |          +--------> PCL.Platform
    |
    +--> PCL.Platform

Future:
PCL.Resources / PCL.Networking / PCL.Modpacks
    |
    +--> PCL.Core
    +--> PCL.Platform abstractions

Optional PCL.Native backends sit behind managed interfaces.
```

Core projects must not reference `PCL.Desktop`.

## Phase 1 scope

The first bootstrap establishes:

- .NET 10 / C# 14;
- Avalonia 12 desktop shell;
- normalized OS/architecture model;
- Mojang rule evaluation;
- Java compatibility model;
- Windows/macOS/Linux CI;
- tests for cross-platform rule and compatibility semantics.

The next meaningful migration target from PCL2 is the launch engine: version metadata, argument rules, Java resolution and a platform-neutral `LaunchPlan`.

## Planned project split

```text
src/
  PCL.Core/
  PCL.Platform/
  PCL.Minecraft/
  PCL.Resources/
  PCL.Networking/
  PCL.Modpacks/
  PCL.Desktop/

native/
  pcl-native/
```

Platform-specific projects (`PCL.Platform.Windows`, `.MacOS`, `.Linux`) should be introduced when real platform behavior is migrated. Empty platform projects are intentionally avoided.

## Native policy

Native code is admitted only after profiling and comparison against an optimized managed implementation.

A native backend should normally provide at least one of:

- >= 1.5x wall-clock improvement on a user-visible workload;
- >= 250 ms reduction in a common wait path;
- >= 50% peak-memory reduction;
- a material safety/capability benefit unavailable from the managed implementation.

The stable boundary should be a coarse-grained C ABI. Chatty per-file or per-buffer FFI calls are prohibited.

# voxel_playground CMake workflow

This repository can build its GDExtension with CMake while still consuming the official `godot-cpp` submodule.

## Configure and build

Debug build:

```sh
cmake --preset debug
cmake --build --preset build-debug
```

Release build:

```sh
cmake --preset release
cmake --build --preset build-release
```

The generated shared library and manifest are written into `project/bin/` so Godot can load them directly.
The configure step also creates a root-level `compile_commands.json` symlink that clang-based tools can pick up.

## Files added

- `CMakeLists.txt`: top-level external build entry point.
- `CMakePresets.json`: ready-to-use debug and release presets.
- `project/bin/voxel_playground.gdextension`: generated during configure.
- `src/`: minimal C++ extension scaffold.

## Notes

- `godot-cpp` still performs the binding generation; CMake only replaces the outer project build orchestration.
- The manifest names are generated from the current platform and architecture to match `godot-cpp` naming.
- Build both presets at least once if you want both debug and release entries in Godot to resolve to real binaries.
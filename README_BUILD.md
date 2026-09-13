# Build from source

Use Windows x64 and the .NET SDK pinned in `global.json` (10.0.400). End users should download the portable release ZIP instead.

```powershell
./tests/build_public.ps1 -ValidateOnly
./tests/build_public.ps1
```

The script restores and publishes a self-contained single-file executable, checks bilingual resources, and runs synthetic fixtures in an isolated output directory. It never replaces a running installation. Outputs and the portable package are under `artifacts/public-build/`.

Build inputs include every C# file in `src/`, `tests/TestHarness.cs`, all eight `src/Resources/*.resx` files, all four `src/Assets/*.png` backgrounds, the manifest, and the project file. Backgrounds are project artwork; required runtime notices remain under `licenses/` and retain their original terms. No SDK, personal settings, history, auth files, or private evidence belongs in the source tree or user ZIP.

Published v0.4.1 uses informational version `0.4.1+gold`. The release assets contain the authoritative executable and ZIP checksums. Independent builds can differ in bytes; do not infer byte identity from a version string alone.

The four decorative backgrounds were generated for this project with image generation; UI controls, charts and screenshot data are rendered by the application. No fonts are redistributed.

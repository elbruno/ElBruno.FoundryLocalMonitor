# Hicks — History

## Session: 2026-07-03 — Team Kickoff

**Requested by:** elbruno (via Squad Coordinator)

### Context
DevOps/NuGet for ElBruno.FoundryLocalMonitor. Responsible for packaging the WPF app as a dotnet global tool and publishing to NuGet.

### Key learnings
- dotnet tool for WPF requires `net9.0-windows` target and `[STAThread]` entry point
- `PackAsTool=true` + `ToolCommandName=foundry-monitor` in the `.Tool` csproj
- NuGet package ID: `ElBruno.FoundryLocalMonitor`
- Install command for end users: `dotnet tool install -g ElBruno.FoundryLocalMonitor`
- CI needs `windows-latest` runner (WPF is Windows-only)
- Versioning: start with `1.0.0`, use tag-based releases (`v1.0.0` tag triggers NuGet publish)
- Reference OllamaMonitor has a `build/` folder for build scripts
- `global.json` should pin SDK to .NET 9

### Decisions made
- CI: GitHub Actions on `windows-latest` for build+test+pack
- CD: Tag-triggered workflow for NuGet publish using `NUGET_API_KEY` secret
- Version source: manual in csproj initially; consider MinVer later

## Session: 2026-07-03 — Phase 1 Delivery

### Work completed
- `global.json` — pinned .NET SDK to 10.0.301
- `.Tool.csproj` — NuGet metadata (`PackAsTool=true`, `ToolCommandName=foundry-monitor`, ID `ElBruno.FoundryLocalMonitor`)
- `Directory.Build.targets` — pack workaround for WPF tool projects
- `.github/workflows/build.yml` — CI: build + test on `windows-latest`
- `.github/workflows/publish.yml` — CD: NuGet publish triggered by `v*` tags
- `.gitignore` — updated for build artifacts
- Pack result: ✅ `ElBruno.FoundryLocalMonitor.0.1.0.nupkg` produced

### Phase 1 status
**Complete.** NuGet packaging and CI/CD pipelines operational.

# Web tests

Run the host-runtime resolver/stream tests with:

```powershell
dotnet test --project tests/EsmParser.Web.Tests/EsmParser.Web.Tests.csproj
```

## Opt-in BSA browser smoke test

`BrowserSmokeTests` uses TUnit and the centrally versioned `Microsoft.Playwright`
NuGet package. It skips unless `BSA_SMOKE_DATA_ROOT` is set. Once enabled, missing
or invalid settings/fixtures fail the test rather than silently skipping it.
No npm installation or JavaScript test runner is needed for this smoke test.

Build the test project first. Use installed Edge/Chrome, or install the matching
Playwright Chromium using the generated .NET Playwright script:

```powershell
dotnet build tests/EsmParser.Web.Tests/EsmParser.Web.Tests.csproj
pwsh artifacts/bin/EsmParser.Web.Tests/debug/playwright.ps1 install chromium
```

Start the app in another terminal from the repository root:

```powershell
dotnet run --project src/EsmParser.Web --no-launch-profile --urls http://127.0.0.1:5187
```

Configure archives containing a model and **all** its dependencies, then run:

```powershell
$env:BSA_SMOKE_DATA_ROOT = 'D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data'
$env:BSA_SMOKE_ARCHIVES = '["Fallout - Meshes.bsa","Fallout - Textures.bsa","Fallout - Textures2.bsa"]'
$env:BSA_SMOKE_MODEL = 'meshes/dungeons/caves/epic/caveepicdoorboth02.nif'
$env:BSA_SMOKE_BROWSER = 'msedge' # chrome also supported; unset for Playwright Chromium
dotnet test --project tests/EsmParser.Web.Tests/EsmParser.Web.Tests.csproj
```

Optional settings: `BSA_SMOKE_URL` (default above), `BSA_SMOKE_HEADED=1`, and
`BSA_SMOKE_OUTPUT` (default `browser-smoke-results` beneath the test output directory).
Each run uses a unique artifact subdirectory, printed in test output. The New Vegas
example is not a default requirement. A WebGPU-capable Chromium environment is required.

The test substitutes only the native folder picker/file handles using an injected
browser shim. C# Playwright route interception supplies bounded slices (up to 64 KiB)
of selected archives; there is no extra file server or whole-archive copy. Production
JavaScript, Blazor, BSA parsing and the WebGPU viewer execute unchanged.

Assertions check indexing, complete dependency resolution, browser errors, actual
archive reads, and renderer acknowledgement. Inspect `model.png` for visual correctness;
acknowledgement is not a pixel-level assertion. Failure screenshots are captured when
possible. Browser/context resources are disposed on failure and success. The separately
started app remains running. Native permission dialogs and loose-file precedence are
covered by manual checks and resolver tests, not this browser smoke test.
Keep screenshots and proprietary fixture data out of commits.

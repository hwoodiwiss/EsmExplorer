using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;

namespace EsmParser.Web.Tests;

public sealed class SkipUnlessBrowserFixtureAttribute() : SkipAttribute("Set BSA_SMOKE_DATA_ROOT to enable the real-BSA browser smoke test.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BSA_SMOKE_DATA_ROOT")));
}

public sealed class BrowserSmokeTests
{
    [Test, SkipUnlessBrowserFixture, Category("BrowserSmoke")]
    public async Task Loads_archived_model_and_all_dependencies_in_browser()
    {
        string root = Path.GetFullPath(Required("BSA_SMOKE_DATA_ROOT"));
        string[] names = JsonSerializer.Deserialize<string[]>(Required("BSA_SMOKE_ARCHIVES"))
            ?? throw new InvalidOperationException("BSA_SMOKE_ARCHIVES must be a JSON array of filenames.");
        await Assert.That(names.Length > 0).IsTrue();
        await Assert.That(names.Distinct(StringComparer.OrdinalIgnoreCase).Count()).IsEqualTo(names.Length);
        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', ':']) >= 0
                || !name.EndsWith(".bsa", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Use top-level BSA filenames in BSA_SMOKE_ARCHIVES.");
            }
            sizes.Add(name, new FileInfo(Path.Combine(root, name)).Length);
        }
        string model = Required("BSA_SMOKE_MODEL").Replace('\\', '/').ToLowerInvariant();
        await Assert.That(model.StartsWith("meshes/", StringComparison.Ordinal) && model.EndsWith(".nif", StringComparison.Ordinal)).IsTrue();
        var appUrl = new Uri(Environment.GetEnvironmentVariable("BSA_SMOKE_URL") ?? "http://127.0.0.1:5187");
        if (!appUrl.IsLoopback || appUrl.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("BSA_SMOKE_URL must be a local HTTP(S) test server.");
        }
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("BSA_SMOKE_OUTPUT")
            ?? Path.Combine(AppContext.BaseDirectory, "browser-smoke-results"));
        // Isolate artifacts so concurrent or repeated test runs cannot overwrite one another.
        output = Path.Combine(output, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var response = await client.GetAsync(appUrl);
        response.EnsureSuccessStatusCode();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Channel = Environment.GetEnvironmentVariable("BSA_SMOKE_BROWSER"),
            Headless = Environment.GetEnvironmentVariable("BSA_SMOKE_HEADED") != "1",
            Args = ["--enable-unsafe-webgpu"],
        });
        await using var context = await browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 1000 },
            ServiceWorkers = ServiceWorkerPolicy.Block,
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(180000);
        var errors = new ConcurrentQueue<string>();
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.PageError += (_, error) => errors.Enqueue(error);
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                errors.Enqueue(message.Text);
            }
            if (message.Text.Contains($"loaded NIF model: {model}", StringComparison.Ordinal))
            {
                rendered.TrySetResult();
            }
        };

        int reads = 0;
        string routePrefix = new Uri(appUrl, $"/__bsa-smoke/{Guid.NewGuid():N}/").AbsoluteUri;
        // Playwright fulfills only this page's synthetic file-slice requests. No extra server,
        // port, CORS policy, or whole-archive transfer is needed.
        await context.RouteAsync(routePrefix + "**", async route =>
        {
            try
            {
                string[] parts = route.Request.Url[routePrefix.Length..].Split('/');
                if (route.Request.Method != "GET" || parts.Length != 3
                    || !sizes.TryGetValue(Uri.UnescapeDataString(parts[0]), out long size)
                    || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long start)
                    || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int count)
                    || count is <= 0 or > 65536 || start > size || count > size - start)
                {
                    errors.Enqueue("Invalid archive range request.");
                    await route.FulfillAsync(new() { Status = 416 });
                    return;
                }
                string name = Uri.UnescapeDataString(parts[0]);
                await using var file = new FileStream(Path.Combine(root, name), FileMode.Open, FileAccess.Read,
                    FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.RandomAccess);
                file.Position = start;
                byte[] bytes = new byte[count];
                await file.ReadExactlyAsync(bytes);
                Interlocked.Increment(ref reads);
                await route.FulfillAsync(new() { Status = 200, ContentType = "application/octet-stream", BodyBytes = bytes });
            }
            catch (Exception ex)
            {
                errors.Enqueue(ex.ToString());
                await route.AbortAsync();
            }
        });

        // Minimal browser-side shim for APIs that native picker automation cannot supply.
        // Test orchestration, file I/O, assertions and lifecycle are all C#/TUnit.
        string settings = JsonSerializer.Serialize(new { names, sizes, routePrefix });
        await page.AddInitScriptAsync("const bsaSmoke = " + settings + ";" + """
            (() => {
              const { names, sizes, routePrefix } = bsaSmoke;
              const entries = names.map(name => ({ name, kind: 'file', getFile: async () => ({
                size: sizes[name], slice: (start, end) => ({ arrayBuffer: async () => {
                  const response = await fetch(`${routePrefix}${encodeURIComponent(name)}/${start}/${end - start}`);
                  if (!response.ok) { throw new Error(`Archive range read failed: ${response.status}`); }
                  return response.arrayBuffer();
                } }),
              }) }));
              window.showDirectoryPicker = async () => ({ name: 'Data', kind: 'directory',
                async *values() { yield* entries; },
                async getFileHandle(name) {
                  const entry = entries.find(e => e.name === name);
                  if (!entry) { throw new DOMException('Missing file', 'NotFoundError'); }
                  return entry;
                },
                async getDirectoryHandle() { throw new DOMException('No loose assets', 'NotFoundError'); },
              });
            })();
            """);
        try
        {
            await page.GotoAsync(new Uri(appUrl, "/model").AbsoluteUri);
            await page.GetByRole(AriaRole.Button, new() { Name = "Choose data root…" }).ClickAsync();
            await page.GetByText($"Data root 'Data' selected; {names.Length} BSA archives indexed.", new() { Exact = true }).WaitForAsync();
            await page.GetByRole(AriaRole.Textbox, new() { Name = "Data-relative model path" }).FillAsync(model);
            await page.GetByRole(AriaRole.Button, new() { Name = "Load path", Exact = true }).ClickAsync();
            // The exact success status excludes partial loads reporting missing dependencies.
            await page.GetByText($"Loaded {model}.", new() { Exact = true }).WaitForAsync();
            await rendered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
            await Assert.That(errors.ToArray()).IsEmpty();
            await Assert.That(reads > 0).IsTrue();
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, "model.png"), FullPage = true });
            Console.WriteLine($"Loaded {model}: {names.Length} archives, {reads} bounded reads, no missing dependencies. Screenshot: {output}");
        }
        catch
        {
            try
            {
                await page.ScreenshotAsync(new() { Path = Path.Combine(output, "failure.png"), FullPage = true });
            }
            catch (PlaywrightException ex)
            {
                Console.WriteLine($"Could not capture failure screenshot: {ex.Message}");
            }
            Console.WriteLine($"Browser errors: {string.Join(Environment.NewLine, errors)}. Artifacts: {output}");
            throw;
        }
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value : throw new InvalidOperationException($"Set {name}; see tests/EsmParser.Web.Tests/README.md.");
}

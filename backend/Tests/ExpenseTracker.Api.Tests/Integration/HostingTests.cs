using System.Net;

namespace ExpenseTracker.Api.Tests.Integration;

// The production image serves the built React app from wwwroot next to the API.
[Collection("api")]
public class HostingTests
{
    private readonly ApiFixture _fx;

    public HostingTests(ApiFixture fx) => _fx = fx;

    [Fact]
    public async Task BuiltAppFilesAreServed_ClientRoutesGetTheApp_AndUnknownApiPathsStay404()
    {
        var root = Directory.CreateTempSubdirectory("et-wwwroot-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "assets"));
            await File.WriteAllTextAsync(Path.Combine(root, "index.html"), "<!doctype html><div id=\"root\"></div><script type=\"module\" src=\"/assets/app.js\"></script>");
            await File.WriteAllTextAsync(Path.Combine(root, "assets", "app.js"), "console.log('app')");
            await File.WriteAllTextAsync(Path.Combine(root, "sw.js"), "self.addEventListener('fetch', () => {})");

            await using var factory = _fx.CreateFactory(authPerMinute: 100_000, webRoot: root);
            var client = factory.CreateClient();

            var script = await client.GetAsync("/assets/app.js");
            Assert.Equal(HttpStatusCode.OK, script.StatusCode);
            Assert.Equal("text/javascript", script.Content.Headers.ContentType?.MediaType);
            Assert.Equal("console.log('app')", await script.Content.ReadAsStringAsync());

            var worker = await client.GetAsync("/sw.js");
            Assert.Equal("text/javascript", worker.Content.Headers.ContentType?.MediaType);

            foreach (var path in new[] { "/", "/m/2026-11", "/m/2026-11/expenses", "/settings" })
            {
                var page = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.OK, page.StatusCode);
                Assert.Contains("id=\"root\"", await page.Content.ReadAsStringAsync());
            }

            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/does-not-exist")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/assets/missing.js")).StatusCode);
            Assert.Equal("ok", await client.GetStringAsync("/healthz"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

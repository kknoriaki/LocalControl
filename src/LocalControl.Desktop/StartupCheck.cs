using System.Net;
using System.Text.RegularExpressions;
using LocalControl.Core;
using LocalControl.Server;
using LocalControl.Windows;

namespace LocalControl.Desktop;

internal static class StartupCheck
{
    internal static async Task Run()
    {
        var data = Path.Combine(Path.GetTempPath(), "LocalControl-health-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        try
        {
            await using var computer = new WindowsComputer();
            var trust = new TrustStore(data);
            await using var host = new ControlHost(computer, trust, Path.Combine(AppContext.BaseDirectory, "wwwroot"), data);
            await host.Start();
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false, UseCookies = false }) { BaseAddress = new(host.DesktopUrl), Timeout = TimeSpan.FromSeconds(10) };
            var page = await http.GetAsync("/");
            page.EnsureSuccessStatusCode();
            var html = await page.Content.ReadAsStringAsync();
            if (page.Content.Headers.ContentType?.MediaType != "text/html" || !html.Contains("id=\"root\"")) throw new InvalidDataException("Application HTML missing");
            foreach (Match match in Regex.Matches(html, "(?:src|href)=\"(/[^\"]+)\""))
            {
                var asset = await http.GetAsync(match.Groups[1].Value);
                asset.EnsureSuccessStatusCode();
                if ((await asset.Content.ReadAsByteArrayAsync()).Length == 0) throw new InvalidDataException("Empty asset");
            }
            if ((await http.GetAsync("/api/v1/state")).StatusCode != HttpStatusCode.Unauthorized) throw new InvalidDataException("Anonymous state allowed");
            http.DefaultRequestHeaders.Add("Cookie", "lc_session=" + trust.CreateDesktop().Credential);
            (await http.GetAsync("/api/v1/state")).EnsureSuccessStatusCode();
            if ((await http.GetAsync("/api/v1/unknown")).StatusCode != HttpStatusCode.NotFound) throw new InvalidDataException("API fallback is not 404");
        }
        finally { try { Directory.Delete(data, true); } catch (IOException) { } }
    }
}

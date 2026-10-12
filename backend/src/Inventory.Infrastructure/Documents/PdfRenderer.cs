using Inventory.Application.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace Inventory.Infrastructure.Documents;

public sealed class PdfOptions
{
    public const string Section = "Pdf";

    /// <summary>
    /// Path to Chrome, Chromium or Microsoft Edge. Empty = look in the usual places
    /// (Edge/Chrome on Windows; chromium/google-chrome on Linux).
    /// </summary>
    public string? BrowserPath { get; set; }

    /// <summary>Needed when the API runs as root in a container.</summary>
    public bool NoSandbox { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Renders at the same time (each uses a browser tab).</summary>
    public int MaxConcurrent { get; set; } = 2;
}

/// <summary>
/// HTML → PDF with a headless Chromium-based browser (PuppeteerSharp). JavaScript is disabled and every network
/// request is blocked, so templates can only use inline styles and embedded (data:) images.
/// </summary>
internal sealed partial class PuppeteerPdfRenderer(IOptions<PdfOptions> options, ILogger<PuppeteerPdfRenderer> logger) : IPdfRenderer, IAsyncDisposable
{
    private readonly SemaphoreSlim _launch = new(1, 1);
    private readonly SemaphoreSlim _slots = new(Math.Max(1, options.Value.MaxConcurrent));
    private IBrowser? _browser;

    private const string Footer =
        "<div style=\"width:100%;font-family:Arial,sans-serif;font-size:7pt;color:#6b7280;padding:0 14mm;text-align:right\">" +
        "<span class=\"pageNumber\"></span> / <span class=\"totalPages\"></span></div>";

    public async Task<byte[]> RenderAsync(string html, CancellationToken cancellationToken)
    {
        await _slots.WaitAsync(cancellationToken);
        try
        {
            var browser = await BrowserAsync(cancellationToken);
            await using var page = await browser.NewPageAsync();
            var timeout = (int)TimeSpan.FromSeconds(options.Value.TimeoutSeconds).TotalMilliseconds;
            page.DefaultTimeout = timeout;
            await page.SetJavaScriptEnabledAsync(false);
            await page.SetRequestInterceptionAsync(true);
            page.Request += async (_, e) =>
            {
                if (e.Request.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || e.Request.Url == "about:blank") await e.Request.ContinueAsync();
                else await e.Request.AbortAsync();
            };
            await page.SetContentAsync(html, new SetContentOptions { Timeout = timeout });
            return await page.PdfDataAsync(new PuppeteerSharp.PdfOptions
            {
                Format = PaperFormat.A4,
                PrintBackground = true,
                PreferCSSPageSize = true,
                DisplayHeaderFooter = true,
                HeaderTemplate = "<div></div>",
                FooterTemplate = Footer,
                MarginOptions = new MarginOptions { Top = "16mm", Bottom = "18mm", Left = "14mm", Right = "14mm" },
            });
        }
        finally
        {
            _slots.Release();
        }
    }

    private async Task<IBrowser> BrowserAsync(CancellationToken cancellationToken)
    {
        if (_browser is { IsConnected: true }) return _browser;
        await _launch.WaitAsync(cancellationToken);
        try
        {
            if (_browser is { IsConnected: true }) return _browser;
            if (_browser is not null) await _browser.DisposeAsync();
            var path = FindBrowser(options.Value.BrowserPath)
                ?? throw new InvalidOperationException("No Chrome, Chromium or Edge browser was found to create PDFs. Install one or set Pdf:BrowserPath.");
            var args = new List<string> { "--disable-gpu", "--disable-extensions", "--no-first-run", "--disable-dev-shm-usage" };
            if (options.Value.NoSandbox) args.AddRange(["--no-sandbox", "--disable-setuid-sandbox"]);
            _browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true, ExecutablePath = path, Args = [.. args] });
            LogLaunched(logger, path);
            return _browser;
        }
        finally
        {
            _launch.Release();
        }
    }

    internal static string? FindBrowser(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;
        string[] candidates = OperatingSystem.IsWindows()
            ? [
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            ]
            : OperatingSystem.IsMacOS()
                ? ["/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"]
                : ["/usr/bin/chromium", "/usr/bin/chromium-browser", "/usr/bin/google-chrome", "/usr/bin/google-chrome-stable", "/opt/pw-browsers/chromium"];
        return candidates.FirstOrDefault(File.Exists);
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _launch.Dispose();
        _slots.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "PDF browser started: {Path}")]
    private static partial void LogLaunched(ILogger logger, string path);
}

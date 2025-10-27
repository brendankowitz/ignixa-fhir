#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ignixa.Tests.Compatibility.CLI;

/// <summary>
/// Hosts the test results viewer HTML interface with automatic browser launch.
/// Provides a local HTTP server for viewing FHIR compatibility test reports.
/// </summary>
internal static class TestResultsViewerCommand
{
    private const string ESC = "\x1b";

    private static class Color
    {
        public const string Reset = ESC + "[0m";
        public const string Bold = ESC + "[1m";
        public const string Cyan = ESC + "[36m";
        public const string Green = ESC + "[32m";
        public const string Yellow = ESC + "[33m";
        public const string Blue = ESC + "[34m";
        public const string Gray = ESC + "[90m";
    }

    /// <summary>
    /// Starts the test results viewer server and opens it in the default browser.
    /// </summary>
    public static async Task RunViewerAsync(int port = 8080, string? autoLoadReportPath = null)
    {
        PrintHeader();

        var baseUrl = $"http://localhost:{port}";
        var htmlContent = GetEmbeddedHtml();
        string? reportContent = null;

        // Load report file if provided
        if (!string.IsNullOrEmpty(autoLoadReportPath))
        {
            if (File.Exists(autoLoadReportPath))
            {
                try
                {
                    reportContent = await File.ReadAllTextAsync(autoLoadReportPath);
                    Console.WriteLine($"{Color.Green}✓{Color.Reset} Report file loaded: {Color.Cyan}{Path.GetFileName(autoLoadReportPath)}{Color.Reset}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{Color.Yellow}⚠ Could not load report file:{Color.Reset} {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"{Color.Yellow}⚠ Report file not found:{Color.Reset} {autoLoadReportPath}");
            }
        }

        using (var listener = new HttpListener())
        {
            listener.Prefixes.Add($"http://localhost:{port}/");

            try
            {
                listener.Start();
                Console.WriteLine($"{Color.Green}✓{Color.Reset} Test Results Viewer started on {Color.Cyan}{baseUrl}{Color.Reset}");
                Console.WriteLine();

                var launchUrl = baseUrl;
                if (!string.IsNullOrEmpty(reportContent))
                {
                    Console.WriteLine($"{Color.Blue}▶ Report will auto-load when page opens{Color.Reset}");
                }

                Console.WriteLine();
                Console.WriteLine($"{Color.Gray}Launching browser...{Color.Reset}");
                LaunchBrowser(launchUrl);

                Console.WriteLine();
                Console.WriteLine($"{Color.Bold}{Color.Green}Viewer is running!{Color.Reset}");
                Console.WriteLine($"  Open: {Color.Cyan}{launchUrl}{Color.Reset}");
                Console.WriteLine($"  {Color.Gray}Press Ctrl+C to exit{Color.Reset}");
                Console.WriteLine();

                // Handle requests
                await HandleRequests(listener, htmlContent, reportContent);
            }
            catch (HttpListenerException ex)
            {
                Console.WriteLine($"{Color.Yellow}⚠ Error:{Color.Reset} Could not bind to port {port}");
                Console.WriteLine($"   {ex.Message}");
                Console.WriteLine();
                Console.WriteLine($"{Color.Yellow}Try a different port:{Color.Reset}");
                Console.WriteLine($"   fhir-compat.dll --viewer --port 8081");
                Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{Color.Yellow}✗ Error:{Color.Reset} {ex.Message}");
                Environment.Exit(1);
            }
        }
    }

    private static async Task HandleRequests(HttpListener listener, string htmlContent, string? reportContent = null)
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await listener.GetContextAsync();
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 995)
            {
                // Listener was closed
                break;
            }

            var request = context.Request;
            var response = context.Response;

            try
            {
                var path = request.Url?.AbsolutePath ?? "/";

                // Handle root and /index.html
                if (path == "/" || path == "/index.html")
                {
                    response.ContentType = "text/html; charset=utf-8";
                    var buffer = Encoding.UTF8.GetBytes(htmlContent);
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(new ReadOnlyMemory<byte>(buffer));
                }
                // Handle API endpoint to get report data
                else if (path == "/api/report")
                {
                    response.ContentType = "application/json; charset=utf-8";
                    if (!string.IsNullOrEmpty(reportContent))
                    {
                        response.StatusCode = 200;
                        var buffer = Encoding.UTF8.GetBytes(reportContent);
                        response.ContentLength64 = buffer.Length;
                        await response.OutputStream.WriteAsync(new ReadOnlyMemory<byte>(buffer));
                        Console.WriteLine($"{Color.Green}✓ /api/report served successfully{Color.Reset}");
                    }
                    else
                    {
                        response.StatusCode = 404;
                        var notFound = Encoding.UTF8.GetBytes("{\"error\":\"No report loaded\"}");
                        response.ContentLength64 = notFound.Length;
                        await response.OutputStream.WriteAsync(new ReadOnlyMemory<byte>(notFound));
                        Console.WriteLine($"{Color.Yellow}⚠ /api/report requested but no report loaded{Color.Reset}");
                    }
                }
                else
                {
                    response.StatusCode = 404;
                    var notFoundContent = Encoding.UTF8.GetBytes("Not Found");
                    response.ContentLength64 = notFoundContent.Length;
                    await response.OutputStream.WriteAsync(new ReadOnlyMemory<byte>(notFoundContent));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{Color.Gray}[Error handling request] {ex.Message}{Color.Reset}");
                response.StatusCode = 500;
            }
            finally
            {
                response.OutputStream.Close();
            }
        }
    }

    private static void LaunchBrowser(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = $"/c start {url}",
                    CreateNoWindow = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{Color.Yellow}⚠ Could not launch browser automatically:{Color.Reset} {ex.Message}");
            Console.WriteLine($"   Please open manually: {url}");
        }
    }

    private static void PrintHeader()
    {
        Console.WriteLine($"{Color.Bold}{Color.Cyan}╔══════════════════════════════════════════════════════════════╗{Color.Reset}");
        Console.WriteLine($"{Color.Bold}{Color.Cyan}║     FHIR Test Results Viewer                                   ║{Color.Reset}");
        Console.WriteLine($"{Color.Bold}{Color.Cyan}╚══════════════════════════════════════════════════════════════╝{Color.Reset}");
        Console.WriteLine();
    }

    /// <summary>
    /// Gets the embedded HTML content for the viewer.
    /// In production, this would be loaded from a resource or external file.
    /// </summary>
    private static string GetEmbeddedHtml()
    {
        // Try to load from external file first (for development)
        var externalPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "test-results-viewer.html");

        if (File.Exists(externalPath))
        {
            try
            {
                return File.ReadAllText(externalPath);
            }
            catch
            {
                // Fall through to inline version
            }
        }

        // Return minimal inline version for fallback
        return GetMinimalHtml();
    }

    private static string GetMinimalHtml()
    {
        const string html = "<!DOCTYPE html>" +
            "<html><head>" +
            "<meta charset=\"UTF-8\">" +
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">" +
            "<title>FHIR Test Results Viewer</title>" +
            "<style>" +
            "* { box-sizing: border-box; }" +
            "html, body { height: 100%; margin: 0; padding: 0; font-family: system-ui, -apple-system, sans-serif; }" +
            "body { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); min-height: 100vh; padding: 20px; }" +
            ".container { max-width: 1200px; margin: 0 auto; }" +
            ".header { text-align: center; color: white; margin-bottom: 40px; }" +
            ".header h1 { font-size: 2.5em; margin: 0 0 10px 0; }" +
            ".upload-zone { background: white; border-radius: 12px; padding: 40px; text-align: center; border: 2px dashed #667eea; cursor: pointer; }" +
            ".btn { padding: 12px 30px; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; border: none; border-radius: 8px; cursor: pointer; font-weight: 600; font-size: 1em; margin: 10px auto; display: block; }" +
            ".file-input { display: none; }" +
            ".dashboard { background: white; border-radius: 12px; padding: 40px; margin-bottom: 30px; }" +
            ".metric-card { background: #f5f5f5; padding: 20px; border-radius: 8px; border-left: 4px solid #667eea; text-align: center; }" +
            ".metric-value { font-size: 2.2em; font-weight: 700; color: #333; }" +
            "</style>" +
            "</head><body>" +
            "<div class=\"container\">" +
            "<div class=\"header\"><h1>Test Results Viewer</h1></div>" +
            "<div id=\"root\"></div>" +
            "</div>" +
            "<input type=\"file\" id=\"fileInput\" class=\"file-input\" accept=\".json\">" +
            "<script>" +
            "function show(data) {" +
            "var h='<div class=\"dashboard\">';h+='<div>Server: '+(data.ServerUrl||'N/A')+'</div>';h+='<div>Pass Rate: '+((data.PassRate*100).toFixed(1))+'%</div>';h+='</div>';document.getElementById('root').innerHTML=h;" +
            "}" +
            "function load() { " +
            "var z=document.createElement('div');z.className='upload-zone';z.innerHTML='<h2>Drop JSON here</h2>';z.id='z';document.getElementById('root').innerHTML='';document.getElementById('root').appendChild(z);" +
            "var f=document.getElementById('fileInput');z.onclick=()=>f.click();z.ondrop=(e)=>{e.preventDefault();var file=e.dataTransfer.files[0];if(file){var r=new FileReader();r.onload=()=>{try{show(JSON.parse(r.result));}catch(e){alert('Invalid JSON')}}; r.readAsText(file);}};" +
            "z.ondragover=(e)=>e.preventDefault();f.onchange=(e)=>{if(e.target.files[0]){var r=new FileReader();r.onload=()=>{try{show(JSON.parse(r.result));}catch(e){alert('Invalid JSON')}}; r.readAsText(e.target.files[0]);}}" +
            "}" +
            "fetch('/api/report').then(r=>r.ok?r.json():Promise.reject()).then(show).catch(load);" +
            "</script>" +
            "</body></html>";
        return html;
    }
}

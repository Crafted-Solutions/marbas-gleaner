using Duende.IdentityModel;
using Duende.IdentityModel.OidcClient.Browser;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace CraftedSolutions.MarBasGleaner.UI
{
    public class HttpListenerOptions
    {
        public required string AuthURL { get; set; }
        public int Port { get; set; }
        public string? CallbackPath { get; set; }
        public int? Timeout { get; set; }
    };

    public class ExtendedLogoutParameters
    {
        public required string ClientId { get; set; }
        public string? LogoutHint { get; set; }
    };

    public class HttpAuthenticationHandler(ILogger logger, int? port = null, string? path = null) : IBrowser
    {
        public int Port { get; } = port ?? GetRandomUnusedPort();
        private readonly string? _path = path;
        private readonly ILogger _logger = logger;

        public string RedirectURL
        {
            get
            {
                var path = _path ?? String.Empty;
                if (path.StartsWith('/')) path = path[1..];
                return $"http://localhost:{Port}/{path}";
            }
        }

        private static int GetRandomUnusedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public async Task<BrowserResult> InvokeAsync(BrowserOptions options, CancellationToken cancellationToken = default)
        {
            var listenerOptions = new HttpListenerOptions()
            {
                AuthURL = options.StartUrl,
                Port = Port,
                CallbackPath = _path,
                Timeout = (int)options.Timeout.TotalSeconds
            };

            // WA for missing custom params in Duende
            var uri = new Uri(options.StartUrl);
            var query = QueryHelpers.ParseQuery(uri.Query);
            if (query.ContainsKey(OidcConstants.EndSessionRequest.PostLogoutRedirectUri))
            {
                var stateParam = (string?)query[OidcConstants.EndSessionRequest.State];
                if (!string.IsNullOrEmpty(stateParam))
                {
                    var state = JsonSerializer.Deserialize<ExtendedLogoutParameters>(stateParam);
                    if (null != state)
                    {
                        var qb = new QueryBuilder(query.Where(x => OidcConstants.EndSessionRequest.State != x.Key))
                        {
                            { "client_id", state.ClientId }
                        };
                        if (!string.IsNullOrEmpty(state.LogoutHint))
                        {
                            qb.Add("logout_hint", state.LogoutHint);
                        }
                        listenerOptions.AuthURL = uri.GetComponents(UriComponents.Scheme | UriComponents.Host | UriComponents.Port | UriComponents.Path, UriFormat.UriEscaped)
                            + qb.ToString();
                    }
                }
            }

            using (var listener = new LoopbackHttpListener(listenerOptions, _logger))
            {
                await listener.Start(cancellationToken);
                OpenBrowser(listener.Url);

                try
                {
                    var result = await listener.WaitForCallbackAsync(cancellationToken);
                    if (string.IsNullOrWhiteSpace(result))
                    {
                        return new BrowserResult { ResultType = BrowserResultType.UnknownError, Error = "Empty response." };
                    }

                    return new BrowserResult { Response = result, ResultType = BrowserResultType.Success };
                }
                catch (TaskCanceledException ex)
                {
                    return new BrowserResult { ResultType = BrowserResultType.Timeout, Error = ex.Message };
                }
                catch (Exception ex)
                {
                    return new BrowserResult { ResultType = BrowserResultType.UnknownError, Error = ex.Message };
                }
            }
        }

        public static void OpenBrowser(string url)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
        }
    }

    public class LoopbackHttpListener : IDisposable
    {
        const int DefaultTimeout = 60 * 5; // 5 mins (in seconds)
        const string ControllerPath = "/controller";

        public string AppName { get; set; } = Process.GetCurrentProcess().ProcessName;
        public string Url => _controllerUrl;

        private readonly IWebHost _host;
        private readonly TaskCompletionSource<string> _source = new();
        private readonly string _workerUrl;
        private readonly string _controllerUrl;
        private readonly string _authUrl;
        private readonly int _timeout;

        private readonly ILogger _logger;

        private bool _disposed = false;

        public LoopbackHttpListener(HttpListenerOptions options, ILogger logger)
        {
            _authUrl = options.AuthURL;
            _timeout = options.Timeout ?? DefaultTimeout;
            _logger = logger;
            var path = options.CallbackPath ?? String.Empty;
            if (path.StartsWith('/')) path = path[1..];

            _workerUrl = $"http://localhost:{options.Port}/{path}";
            _controllerUrl = $"http://localhost:{options.Port}{ControllerPath}";
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Starting listener at {worker} and {controller}", _workerUrl, _controllerUrl);
            }

            _host = new WebHostBuilder()
                .UseKestrel()
                .UseUrls(_workerUrl)
                .Configure(Configure)
                .Build();
        }

        public async Task Start(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await _host.StartAsync(cancellationToken);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                Task.Run(async () =>
                {
                    await Task.Delay(500).ConfigureAwait(false);
                    _host.Dispose();
                });
            }
            _disposed = true;
        }

        private void Configure(IApplicationBuilder app)
        {
            app.Run(async (ctx) =>
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Incoming request {method} {path}", ctx.Request.Method, ctx.Request.Path);
                }
                if (ControllerPath == ctx.Request.Path)
                {
                    await GetController(ctx, ctx.RequestAborted);
                }
                else if (!ctx.Request.Path.HasValue || "/" == ctx.Request.Path)
                {
                    if (ctx.Request.Method == "GET")
                    {
                        var result = ctx.Request.QueryString.Value ??
                            throw new InvalidOperationException("QueryString cannot be null");
                        await SetResultAsync(result, ctx, ctx.RequestAborted);
                    }
                    else if (ctx.Request.Method == "POST")
                    {
                        if (ctx.Request.HasFormContentType)
                        {
                            using (var sr = new StreamReader(ctx.Request.Body, Encoding.UTF8))
                            {
                                var body = await sr.ReadToEndAsync(ctx.RequestAborted);
                                await SetResultAsync(body, ctx, ctx.RequestAborted);
                            }
                        }
                        else
                        {
                            ctx.Response.StatusCode = (int)HttpStatusCode.UnsupportedMediaType;
                            _source.TrySetCanceled(ctx.RequestAborted);
                        }
                    }
                    else
                    {
                        ctx.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    }
                }
                else
                {
                    ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
                }
            });
        }

        private async Task GetController(HttpContext ctx, CancellationToken cancellationToken = default)
        {

            var html = @$"
<!DOCTYPE html><html>
<head>
	<meta charset=""UTF-8"">
	<title>{string.Format(HttpAuthenticationHandlerL10n.TitleLoginController, AppName)}</title>
	<script type=""text/javascript"">
        function openWorker() {{
            const result = window.open('{_authUrl}', 'worker', 'width=800,height=640');
            if (result && !result.closed) {{
                let timer = setInterval(function() {{   
                    if (result.closed) {{  
                        clearInterval(timer);
                        window.close();
                    }}  
                }}, 1000);
            }}
            return result;
        }}
        let success = false;
        window.addEventListener('beforeunload', () => {{
            if (!success) navigator.sendBeacon('/', '');
        }});
		window.addEventListener('message', (evt) => {{
			if ('login-complete' == evt.data) {{
                success = true;
				worker.close();
				window.close();
			}}
			const elm = document.querySelector('h1');
			elm.textContent = '{HttpAuthenticationHandlerL10n.MsgComplete}';
		}});
		let worker = openWorker();
	</script>
</head>
<body>
	<h1>{HttpAuthenticationHandlerL10n.MsgWaiting}</h1>
    <script type=""text/javascript"">
        if (!worker) {{
            const elm = document.createElement('p');
            elm.innerHTML = ""{string.Format(HttpAuthenticationHandlerL10n.HintPopupBlocker, _authUrl)}"";
            document.body.appendChild(elm);
        }}
    </script>
</body>
</head>
</html>";
            ctx.Response.StatusCode = (int)HttpStatusCode.OK;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(html, Encoding.UTF8, cancellationToken);
            await ctx.Response.Body.FlushAsync(cancellationToken);
        }

        private async Task SetResultAsync(string value, HttpContext ctx, CancellationToken cancellationToken = default)
        {
            try
            {
                var html = @$"
<!DOCTYPE html><html>
<head>
	<meta charset=""UTF-8"">
	<title>{string.Format(HttpAuthenticationHandlerL10n.TitleAuthProcessor, AppName)}</title>
	<script type=""text/javascript"">
		window.opener.postMessage('login-complete');
	</script>
</head>
<body>
	<h1>{HttpAuthenticationHandlerL10n.MsgProcessing}</h1>
</body>
</html>";
                ctx.Response.StatusCode = (int)HttpStatusCode.OK;
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.WriteAsync(html, Encoding.UTF8, cancellationToken);
                await ctx.Response.Body.FlushAsync(cancellationToken);

                _source.TrySetResult(value);
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Error))
                {
                    _logger.LogError(ex, "Error processing authorization response");
                }

                ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                ctx.Response.ContentType = "text/html";
                await ctx.Response.WriteAsync("<h1>Invalid request.</h1>", Encoding.UTF8, cancellationToken);
                await ctx.Response.Body.FlushAsync(cancellationToken);
            }
        }

        public Task<string> WaitForCallbackAsync(CancellationToken cancellationToken = default)
        {
            Task.Run(async () =>
            {
                await Task.Delay(_timeout * 1000, cancellationToken).ConfigureAwait(false);
                _source.TrySetCanceled(cancellationToken);
            }, cancellationToken);

            return _source.Task;
        }
    }
}
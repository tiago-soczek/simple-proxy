using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SimpleProxy
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpClient();
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            app.UseWhen(x => x.Request.Headers["x-sql"] == "true", HandleSql);
            app.UseWhen(x => x.Request.Headers["x-sql"] != "true", HandleHttp);
        }

        private void HandleHttp(IApplicationBuilder app)
        {
            app.Use(async (context, next) =>
            {
                var host = context.Request.Headers["x-host"];

                if (string.IsNullOrWhiteSpace(host))
                {
                    context.Response.StatusCode = 400;

                    await context.Response.WriteAsync("Provide x-host");

                    return;
                }

                var xPort = context.Request.Headers["x-port"];
                var port = context.Request.Host.Port ?? 80;

                if (!string.IsNullOrWhiteSpace(xPort) && !int.TryParse(xPort, out port))
                {
                    context.Response.StatusCode = 400;

                    await context.Response.WriteAsync("Provide valid x-port");

                    return;
                }

                string scheme = context.Request.Headers["x-scheme"];

                if (string.IsNullOrEmpty(scheme))
                {
                    scheme = context.Request.Scheme;
                }
                else if (!"http".Equals(scheme) && !"https".Equals(scheme))
                {
                    context.Response.StatusCode = 400;

                    await context.Response.WriteAsync("x-scheme: Only 'http' and 'https' schemes are allowed");

                    return;
                }

                var forwardHost = new HostString(host, port);

                var uri = new Uri(UriHelper.BuildAbsolute(scheme, forwardHost, context.Request.PathBase, context.Request.Path, context.Request.QueryString));

                using (var requestMessage = context.CreateProxyHttpRequest(uri))
                {
                    try
                    {
                        using (var responseMessage = await context.SendProxyHttpRequest(requestMessage))
                        {
                            await context.CopyProxyHttpResponse(responseMessage);
                        }
                    }
                    catch (Exception e)
                    {
                        context.Response.StatusCode = 500;
                        await context.Response.WriteAsync(e.ToString());
                    }
                }
            });
        }

        private void HandleSql(IApplicationBuilder app)
        {
            app.Use(async (context, next) =>
            {
                var connectionString = context.Request.Headers["x-connection-string"];

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    context.Response.StatusCode = 400;

                    await context.Response.WriteAsync("Provide x-connection-string (example: 'Server=tiagso-5510;Database=master;User Id=sa;Password=yourStrong(!)Password;')");

                    return;
                }

                string commandText = context.Request.Headers["x-command-text"];

                if (string.IsNullOrWhiteSpace(commandText))
                {
                    context.Response.StatusCode = 400;

                    await context.Response.WriteAsync("Provide x-command-text (scalar query: SELECT @servername)");

                    return;
                }

                try
                {
                    using (var conn = new SqlConnection(connectionString))
                    {
                        var sw = Stopwatch.StartNew();

                        await conn.OpenAsync();

                        var openDuration = sw.Elapsed;

                        sw.Restart();

                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = commandText;

                            var output = await cmd.ExecuteScalarAsync();

                            context.Response.StatusCode = 200;

                            var executeDuration = sw.Elapsed;

                            sw.Stop();

                            await context.Response.WriteAsync($@"Output: {output}{Environment.NewLine}");
                            await context.Response.WriteAsync($"Open Connection Duration: {openDuration.TotalMilliseconds}ms{Environment.NewLine}");
                            await context.Response.WriteAsync($"Execute Command Duration: {executeDuration.TotalMilliseconds}ms{Environment.NewLine}");
                        }
                    }
                }
                catch (Exception e)
                {
                    context.Response.StatusCode = 500;

                    await context.Response.WriteAsync(e.ToString());

                    return;
                }
            });
        }
    }

    internal static class ProxyAdvancedExtensions
    {
        private const int StreamCopyBufferSize = 81920;

        public static HttpRequestMessage CreateProxyHttpRequest(this HttpContext context, Uri uri)
        {
            var request = context.Request;

            var requestMessage = new HttpRequestMessage();
            var requestMethod = request.Method;
            if (!HttpMethods.IsGet(requestMethod) &&
                !HttpMethods.IsHead(requestMethod) &&
                !HttpMethods.IsDelete(requestMethod) &&
                !HttpMethods.IsTrace(requestMethod))
            {
                var streamContent = new StreamContent(request.Body);
                requestMessage.Content = streamContent;
            }

            // Copy the request headers
            foreach (var header in request.Headers)
            {
                if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && requestMessage.Content != null)
                {
                    requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                }
            }

            requestMessage.Headers.Host = uri.Authority;
            requestMessage.RequestUri = uri;
            requestMessage.Method = new HttpMethod(request.Method);

            return requestMessage;
        }


        public static Task<HttpResponseMessage> SendProxyHttpRequest(this HttpContext context, HttpRequestMessage requestMessage)
        {
            if (requestMessage == null)
            {
                throw new ArgumentNullException(nameof(requestMessage));
            }

            // TODO: Resolve
            var httpClient = new HttpClient();

            return httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        }

        public static async Task CopyProxyHttpResponse(this HttpContext context, HttpResponseMessage responseMessage)
        {
            if (responseMessage == null)
            {
                throw new ArgumentNullException(nameof(responseMessage));
            }

            var response = context.Response;

            response.StatusCode = (int)responseMessage.StatusCode;
            foreach (var header in responseMessage.Headers)
            {
                response.Headers[header.Key] = header.Value.ToArray();
            }

            foreach (var header in responseMessage.Content.Headers)
            {
                response.Headers[header.Key] = header.Value.ToArray();
            }

            // SendAsync removes chunking from the response. This removes the header so it doesn't expect a chunked response.
            response.Headers.Remove("transfer-encoding");

            using (var responseStream = await responseMessage.Content.ReadAsStreamAsync())
            {
                await responseStream.CopyToAsync(response.Body, StreamCopyBufferSize, context.RequestAborted);
            }
        }
    }
}

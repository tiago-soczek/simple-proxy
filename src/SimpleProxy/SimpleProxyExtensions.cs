using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Primitives;
using System;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SimpleProxy
{
    public static class SimpleProxyExtensions
    {
        private const string SqlKey = "x-sql";
        private const string HostKey = "x-host";
        private const string PortKey = "x-port";
        private const string SchemeKey = "x-scheme";
        private const string ConnectionStringKey = "x-connection-string";
        private const string CommandTextKey = "x-command-text";
        private const string TrueKey = "true";

        public static void UseSimpleProxy(this IApplicationBuilder app)
        {
            app.UseWhen(x => x.Request.Headers[SqlKey] == TrueKey, builder => builder.Use((c,n) => HandleSql(c)));
            app.UseWhen(x => x.Request.Headers[SqlKey] != TrueKey, builder => builder.Use((c,n) => HandleHttp(c)));
        }

        private static async Task HandleHttp(HttpContext context)
        {
            // Get Parameters
            var host = context.Request.Headers[HostKey];

            if (string.IsNullOrWhiteSpace(host))
            {
                await context.WriteBadRequest($"Provide {HostKey} header or {SqlKey} header with {TrueKey} value");

                return;
            }

            var xPort = context.Request.Headers[PortKey];
            var port = context.Request.Host.Port ?? 80;

            if (!string.IsNullOrWhiteSpace(xPort) && !int.TryParse(xPort, out port))
            {
                await context.WriteBadRequest($"Provide valid {PortKey}");

                return;
            }

            string scheme = context.Request.Headers[SchemeKey];

            if (string.IsNullOrEmpty(scheme))
            {
                scheme = context.Request.Scheme;
            }
            else if (!"http".Equals(scheme) && !"https".Equals(scheme))
            {
                await context.WriteBadRequest($"{SchemeKey}: Only 'http' and 'https' schemes are allowed");

                return;
            }

            // Execute
            await ForwardRequest(context, host, port, scheme);
        }

        private static async Task HandleSql(HttpContext context)
        {
            // Get Parameters
            var connectionString = context.Request.Headers[ConnectionStringKey];

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                await context.WriteBadRequest($"Provide {ConnectionStringKey} (example: 'Server=tiagso-5510;Database=master;User Id=sa;Password=yourStrong(!)Password;')");

                return;
            }

            string commandText = context.Request.Headers[CommandTextKey];

            if (string.IsNullOrWhiteSpace(commandText))
            {
                await context.WriteBadRequest($"Provide {CommandTextKey} (scalar query: SELECT @servername)");

                return;
            }

            // Execute
            await ExecuteSql(context, connectionString, commandText);
        }

        private static async Task ForwardRequest(HttpContext context, StringValues host, int port, string scheme)
        {
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
                    await context.WriteInternalServerError(e);
                }
            }
        }

        private static async Task ExecuteSql(HttpContext context, string connectionString, string commandText)
        {
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
                        await context.Response.WriteAsync($"Execute Command Duration: {executeDuration.TotalMilliseconds}ms");
                    }
                }
            }
            catch (Exception e)
            {
                await context.WriteInternalServerError(e);
            }
        }
    }
}

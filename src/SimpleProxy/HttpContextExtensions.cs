using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;

namespace SimpleProxy
{
    internal static class HttpContextExtensions
    {
        public static Task WriteBadRequest(this HttpContext context, string message)
        {
            context.Response.StatusCode = 400;

            return context.Response.WriteAsync(message);
        }

        public static Task WriteInternalServerError(this HttpContext context, Exception e)
        {
            context.Response.StatusCode = 500;

            return context.Response.WriteAsync(e.ToString());
        }
    }

}

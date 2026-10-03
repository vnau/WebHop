using WebHop.Gateway.Host;

// Same namespace as UseStatusCodePages, so UseWebHopErrorPages() needs no extra using
namespace Microsoft.AspNetCore.Builder
{
    internal static class GatewayErrorPageExtensions
    {
        /// <summary>
        /// Serves the WebHop-branded HTML page for the gateway's error responses (502/503/504). Other
        /// status codes are left untouched. Host-only branding — not part of the WebHop.Gateway package.
        /// </summary>
        public static IApplicationBuilder UseWebHopErrorPages(this IApplicationBuilder app)
        {
            return app.UseStatusCodePages(async context =>
            {
                var response = context.HttpContext.Response;
                if (response.StatusCode is 502 or 503 or 504)
                {
                    response.ContentType = "text/html; charset=utf-8";
                    await response.WriteAsync(GatewayErrorPage.Render(response.StatusCode));
                }
            });
        }
    }
}

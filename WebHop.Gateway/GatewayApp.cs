namespace WebHop.Gateway
{
    /// <summary>
    /// The standalone gateway app: a thin Kestrel host over <c>AddWebHopGateway</c> /
    /// <c>UseWebHopGateway</c>. Program runs it, tests start it in-process. To embed the gateway in
    /// another app instead, call those two extensions directly and serve your own content after.
    /// </summary>
    public static class GatewayApp
    {
        /// <param name="configure">
        /// Optional host setup, applied after the services are built and before the gateway's own
        /// middleware. Use it for things that must run first, such as branded status-code pages or
        /// static files — host concerns that stay out of the reusable middleware package.
        /// </param>
        public static WebApplication Create(string[] args, Action<WebApplication>? configure = null)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = null;
            });
            // Under IIS (Azure App Service on Windows, in-process) requests are served by IIS's server instead of Kestrel
            builder.Services.Configure<IISServerOptions>(options =>
            {
                options.MaxRequestBodySize = null;
            });

            builder.Services.AddWebHopGateway();

            var app = builder.Build();

            configure?.Invoke(app);

            app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
            app.UseWebHopGateway();

            // A standalone gateway has no local content, so an unforwarded request means no server is connected
            app.Run(context =>
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return Task.CompletedTask;
            });

            return app;
        }
    }
}

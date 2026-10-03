using WebHop.Gateway;

GatewayApp.Create(args, app => app.UseWebHopErrorPages()).Run();

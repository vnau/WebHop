using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Microsoft.Extensions.Logging;
using WebHop.Core.Extensions;

namespace WebHop.Server
{
    internal class WebHopRequestProcessor<TContext>(IHttpApplication<TContext> application) : IApplicationProcessor where TContext : notnull
    {
        public async Task<Core.Models.HttpResponseMessage> ProcessRequestAsync(
            IFeatureCollection features,
            Core.Models.HttpRequestMessage request,
            ILogger logger,
            CancellationToken ct)
        {
            features.Set<IEndpointFeature>(null);
            features.Set<IHttpRequestFeature>(new HttpRequestFeature());
            features.Set<IHttpResponseFeature>(new HttpResponseFeature());
            features.Set<IHttpAuthenticationFeature>(new HttpAuthenticationFeature());

            var requestFeature = features.Get<IHttpRequestFeature>();
            var responseFeature = features.Get<IHttpResponseFeature>();

            HttpRequestMessageToFeature(request, requestFeature);

            using var responseStream = new MemoryStream();
            features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(responseStream));
            Exception? exception = null;
            var context = application.CreateContext(features);
            var requestIdentifier = features.Get<IHttpRequestIdentifierFeature>();
            requestIdentifier.TraceIdentifier = request.Id;
            try
            {
                await application.ProcessRequestAsync(context);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw;
            }
            finally
            {
                application.DisposeContext(context, exception);
            }

            // Reset stream to read response
            responseStream.Position = 0;
            byte[] responseBody = await responseStream.ToArrayAsync(ct);

            // Copy response headers
            var headers = responseFeature.Headers
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.FirstOrDefault() ?? "");

            return new Core.Models.HttpResponseMessage
            {
                Id = request.Id,
                StatusCode = responseFeature.StatusCode,
                Headers = headers,
                Body = responseBody
            };
        }

        private static void HttpRequestMessageToFeature(Core.Models.HttpRequestMessage req, IHttpRequestFeature feature)
        {
            foreach (var v in req.Headers)
            {
                feature.Headers[v.Key] = new Microsoft.Extensions.Primitives.StringValues(v.Value);
            }
            feature.Method = req.Method.ToString();
            feature.Protocol = req.Protocol;
            feature.Path = req.Path;
            feature.PathBase = "";
            feature.QueryString = req.QueryString;
            if (req.Body.Length > 0)
                feature.Body = new MemoryStream(req.Body);
            feature.Scheme = req.Scheme;
        }
    }
}
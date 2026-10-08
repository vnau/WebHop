using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using WebHop.Core;
using WebHop.Origin;

namespace WebHop.Tests.Origin
{
    public class ForwardedHeadersApplicationTests
    {
        [Fact]
        public void Scheme_client_address_and_request_id_come_from_the_gateway_headers()
        {
            var features = Features(new()
            {
                [Headers.XForwardedProto] = "https",
                [Headers.XForwardedFor] = "203.0.113.7:4567",
                [Headers.XWebhopRequestId] = "gateway-request-1",
            });

            new ForwardedHeadersApplication<IFeatureCollection>(new PassThroughApplication()).CreateContext(features);

            Assert.Equal("https", features.Get<IHttpRequestFeature>()!.Scheme);
            Assert.Equal(IPAddress.Parse("203.0.113.7"), features.Get<IHttpConnectionFeature>()!.RemoteIpAddress);
            Assert.Equal(4567, features.Get<IHttpConnectionFeature>()!.RemotePort);
            Assert.Equal("gateway-request-1", features.Get<IHttpRequestIdentifierFeature>()!.TraceIdentifier);
        }

        [Fact]
        public void IPv6_client_addresses_are_understood()
        {
            var features = Features(new() { [Headers.XForwardedFor] = "[2001:db8::1]:443" });

            new ForwardedHeadersApplication<IFeatureCollection>(new PassThroughApplication()).CreateContext(features);

            Assert.Equal(IPAddress.Parse("2001:db8::1"), features.Get<IHttpConnectionFeature>()!.RemoteIpAddress);
            Assert.Equal(443, features.Get<IHttpConnectionFeature>()!.RemotePort);
        }

        [Fact]
        public void Without_gateway_headers_nothing_changes()
        {
            var features = Features([]);
            features.Get<IHttpConnectionFeature>()!.RemoteIpAddress = IPAddress.Loopback;

            new ForwardedHeadersApplication<IFeatureCollection>(new PassThroughApplication()).CreateContext(features);

            Assert.Equal("http", features.Get<IHttpRequestFeature>()!.Scheme);
            Assert.Equal(IPAddress.Loopback, features.Get<IHttpConnectionFeature>()!.RemoteIpAddress);
        }

        [Fact]
        public void An_unparseable_client_address_is_ignored()
        {
            var features = Features(new() { [Headers.XForwardedFor] = "not an address" });
            features.Get<IHttpConnectionFeature>()!.RemoteIpAddress = IPAddress.Loopback;

            new ForwardedHeadersApplication<IFeatureCollection>(new PassThroughApplication()).CreateContext(features);

            Assert.Equal(IPAddress.Loopback, features.Get<IHttpConnectionFeature>()!.RemoteIpAddress);
        }

        private static FeatureCollection Features(Dictionary<string, string> headers)
        {
            var request = new HttpRequestFeature { Scheme = "http" };
            foreach (var (name, value) in headers)
                request.Headers[name] = value;

            var features = new FeatureCollection();
            features.Set<IHttpRequestFeature>(request);
            features.Set<IHttpConnectionFeature>(new HttpConnectionFeature());
            features.Set<IHttpRequestIdentifierFeature>(new HttpRequestIdentifierFeature());
            return features;
        }

        private sealed class PassThroughApplication : IHttpApplication<IFeatureCollection>
        {
            public IFeatureCollection CreateContext(IFeatureCollection contextFeatures) => contextFeatures;
            public Task ProcessRequestAsync(IFeatureCollection context) => Task.CompletedTask;
            public void DisposeContext(IFeatureCollection context, Exception? exception) { }
        }
    }
}

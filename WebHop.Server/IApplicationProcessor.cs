using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using WebHop.Core.Models;

namespace WebHop.Server
{
    internal interface IApplicationProcessor
    {
        Task<Core.Models.HttpResponseMessage> ProcessRequestAsync(
            IFeatureCollection features,
            Core.Models.HttpRequestMessage request,
            ILogger logger,
            CancellationToken ct
        );
    }
}
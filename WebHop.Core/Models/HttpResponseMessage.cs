namespace WebHop.Core.Models
{
    public class HttpResponseMessage
    {
        public required string Id { get; init; }
        public required int StatusCode { get; init; }
        public required Dictionary<string, string> Headers { get; init; }
        public required byte[] Body { get; init; }
    }
}

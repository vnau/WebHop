namespace WebHop.Core.Models
{
    public class HttpRequestMessage
    {
        public required string Id { get; init; } = Guid.NewGuid().ToString();
        public required string Method { get; init; }
        public required string Protocol { get; init; }
        public required string Path { get; init; }
        public required string Scheme { get; init; }
        public required string QueryString { get; init; }
        public required Dictionary<string, string> Headers { get; init; } = [];
        public required byte[] Body { get; init; } = [];
    }
}

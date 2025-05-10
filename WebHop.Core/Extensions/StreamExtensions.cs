namespace WebHop.Core.Extensions
{
    public static class StreamExtensions
    {
        public static async Task<byte[]> ToArrayAsync(this Stream input, CancellationToken ct)
        {
            using MemoryStream ms = new();
            await input.CopyToAsync(ms, ct);
            return ms.ToArray();
        }
    }
}

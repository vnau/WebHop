namespace WebHop.Gateway.Models
{
    public class PendingRequest
    {
        public required HttpContext Context { get; init; }
        public required SemaphoreSlim Semaphore { get; init; }
    }
}

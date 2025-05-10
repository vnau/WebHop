namespace WebHop.Core.Abstract
{
    public interface IMessageHandler<TMessage>
    {
        Task ProcessMessageAsync(TMessage message);
    }
}

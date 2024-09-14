namespace MarBasGleaner.BrokerAPI.Models
{
    public interface IMarBasResult<T>
    {
        bool Success { get; }
        T? Yield { get; }
    }
}

namespace DotBot.Hosting;

public interface IDotBotHost : IAsyncDisposable
{
    Task RunAsync(CancellationToken cancellationToken = default);
}

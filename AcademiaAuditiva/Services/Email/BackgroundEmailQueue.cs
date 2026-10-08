using System.Threading.Channels;

namespace AcademiaAuditiva.Services.Email;

/// <summary>
/// E-mails to send once the request that asked for them is over: <see cref="BackgroundEmailWorker"/>
/// runs the jobs one after the other, each in a scope of its own. The queue lives in memory,
/// so the jobs still waiting when the app stops are lost (the worker logs how many).
/// </summary>
public sealed class BackgroundEmailQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _jobs =
        Channel.CreateUnbounded<Func<IServiceProvider, CancellationToken, Task>>(new UnboundedChannelOptions { SingleReader = true });

    private int _pending;

    /// <summary>Jobs queued and not finished yet.</summary>
    public int Pending => Volatile.Read(ref _pending);

    /// <param name="job">Gets the services of its scope; the token is cancelled when the app stops.</param>
    public void Enqueue(Func<IServiceProvider, CancellationToken, Task> job)
    {
        ArgumentNullException.ThrowIfNull(job);
        Interlocked.Increment(ref _pending);
        // Never false: the channel is unbounded and never completed.
        _jobs.Writer.TryWrite(job);
    }

    internal IAsyncEnumerable<Func<IServiceProvider, CancellationToken, Task>> ReadAllAsync(CancellationToken cancellationToken)
        => _jobs.Reader.ReadAllAsync(cancellationToken);

    internal void Finished() => Interlocked.Decrement(ref _pending);
}

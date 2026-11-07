using System.Collections.Concurrent;

namespace Orknux.Desktop;

/// <summary>
/// One single-threaded apartment thread that every UI Automation call runs on.
/// UIA's COM objects belong to the apartment that made them, so the automation
/// object, the elements of a snapshot and the clicks on them all live here,
/// away from the window's own thread, which stays free to draw the log.
/// </summary>
internal sealed class StaWorker : IDisposable
{
    private readonly BlockingCollection<Action> work = new();
    private readonly Thread thread;

    public StaWorker()
    {
        thread = new Thread(() =>
        {
            foreach (var job in work.GetConsumingEnumerable()) job();
        })
        { IsBackground = true, Name = "UI Automation" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public T Invoke<T>(Func<T> job)
    {
        if (Thread.CurrentThread == thread) return job();
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Add(() =>
        {
            try { done.SetResult(job()); }
            catch (Exception failed) { done.SetException(failed); }
        });
        return done.Task.GetAwaiter().GetResult();
    }

    public void Dispose() => work.CompleteAdding();
}

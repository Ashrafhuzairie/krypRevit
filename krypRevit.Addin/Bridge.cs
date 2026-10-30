using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.UI;

namespace krypRevit.Addin
{
    // Jobs expire only while queued. Once execution starts its actual result is returned.
    public sealed class Bridge : IExternalEventHandler, IDisposable
    {
        private sealed class Job
        {
            public Func<UIApplication, string> Work;
            public readonly TaskCompletionSource<string> Done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            public int State; // 0 queued, 1 executing, 2 cancelled/completed
        }
        private readonly ConcurrentQueue<Job> queue = new ConcurrentQueue<Job>();
        private readonly ExternalEvent externalEvent;
        private readonly object gate = new object();
        private bool disposed;
        public Bridge() { externalEvent = ExternalEvent.Create(this); }
        public async Task<string> Invoke(Func<UIApplication, string> work, int timeoutMs = 30000)
        {
            var job = new Job { Work = work };
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(Bridge));
                queue.Enqueue(job);
                try
                {
                    var result = externalEvent.Raise();
                    if (result != ExternalEventRequest.Accepted && result != ExternalEventRequest.Pending)
                        throw new InvalidOperationException("Revit refused the request; try again when idle.");
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref job.State, 2);
                    job.Done.TrySetException(ex);
                }
            }
            if (await Task.WhenAny(job.Done.Task, Task.Delay(timeoutMs)).ConfigureAwait(false) != job.Done.Task &&
                Interlocked.CompareExchange(ref job.State, 2, 0) == 0)
                job.Done.TrySetException(new TimeoutException("Revit request expired before execution. No action was performed."));
            return await job.Done.Task.ConfigureAwait(false);
        }
        public void Execute(UIApplication app)
        {
            while (queue.TryDequeue(out var job))
            {
                if (Interlocked.CompareExchange(ref job.State, 1, 0) != 0) continue;
                try { job.Done.TrySetResult(job.Work(app)); }
                catch (Exception ex) { job.Done.TrySetException(ex); }
                finally { Interlocked.Exchange(ref job.State, 2); }
            }
        }
        public string GetName() => "krypRevit.Bridge";
        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                while (queue.TryDequeue(out var job))
                {
                    Interlocked.Exchange(ref job.State, 2);
                    job.Done.TrySetException(new ObjectDisposedException(nameof(Bridge)));
                }
                externalEvent.Dispose();
            }
        }
    }
}

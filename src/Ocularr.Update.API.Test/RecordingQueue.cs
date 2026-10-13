using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ocularr.Update.API.TaskQueue;

namespace Ocularr.Update.API.Test
{
    public sealed class RecordingQueue : IBackgroundTaskQueue
    {
        private readonly List<Func<CancellationToken, Task>> _items = new ();
        private readonly TaskCompletionSource<Func<CancellationToken, Task>> _first = new (TaskCreationOptions.RunContinuationsAsynchronously);

        public int Count
        {
            get
            {
                lock (_items)
                {
                    return _items.Count;
                }
            }
        }

        public Task<Func<CancellationToken, Task>> First => _first.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem)
        {
            lock (_items)
            {
                _items.Add(workItem);
            }

            _first.TrySetResult(workItem);
        }

        public async Task<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }
    }
}

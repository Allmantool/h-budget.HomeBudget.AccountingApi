using System;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure
{
    public sealed class SemaphoreGuard : IAsyncDisposable, IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _released;

        private SemaphoreGuard(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public static async Task<SemaphoreGuard> WaitAsync(SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync();
            return new SemaphoreGuard(semaphore);
        }

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _semaphore?.Release();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

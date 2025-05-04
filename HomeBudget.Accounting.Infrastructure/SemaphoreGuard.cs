using System;
using System.Threading;

namespace HomeBudget.Accounting.Infrastructure
{
    public sealed class SemaphoreGuard(SemaphoreSlim semaphore) : IDisposable
    {
        private readonly SemaphoreSlim _semaphore = semaphore;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _semaphore.Release();
            _disposed = true;
        }
    }
}

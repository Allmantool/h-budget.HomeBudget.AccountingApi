using System;
using System.Threading;

namespace HomeBudget.Accounting.Infrastructure
{
    public sealed class SemaphoreGuard(SemaphoreSlim semaphore) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            semaphore.Release();
            _disposed = true;
        }
    }
}

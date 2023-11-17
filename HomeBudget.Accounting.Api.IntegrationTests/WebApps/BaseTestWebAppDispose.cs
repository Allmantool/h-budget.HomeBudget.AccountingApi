using System;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Api.IntegrationTests.WebApps
{
    internal abstract class BaseTestWebAppDispose : IDisposable, IAsyncDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCoreAsync();

            Dispose(false);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _ = DisposeAsyncCoreAsync().AsTask();
            }

            _disposed = true;
        }

        protected abstract ValueTask DisposeAsyncCoreAsync();
    }
}

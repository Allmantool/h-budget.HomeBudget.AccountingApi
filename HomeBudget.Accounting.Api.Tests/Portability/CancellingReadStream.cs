using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Api.Tests.Portability
{
    internal sealed class CancellingReadStream : MemoryStream
    {
        private readonly CancellationTokenSource _cancellation;

        public CancellingReadStream(byte[] buffer, CancellationTokenSource cancellation)
            : base(buffer, false)
        {
            _cancellation = cancellation;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            _cancellation.Cancel();
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = base.Read(buffer);
            _cancellation.Cancel();
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            await _cancellation.CancelAsync().ConfigureAwait(false);
            return read;
        }
    }
}

using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.HealthChecks
{
    internal static class TcpHealthProbe
    {
        public static async Task ProbeAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeoutCts.Token);
        }
    }
}

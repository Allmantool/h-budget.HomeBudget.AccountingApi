using System;
using System.Threading.Tasks;

using Dapper;
using Microsoft.Data.SqlClient;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    internal sealed class SqlMaintenanceAdmissionPermit(
        SqlConnection connection,
        string lockResource) : IMaintenanceAdmissionPermit
    {
        private SqlConnection _connection = connection;

        public async ValueTask DisposeAsync()
        {
            var activeConnection = _connection;
            if (activeConnection is null)
            {
                return;
            }

            _connection = null;
            try
            {
                if (activeConnection.State == System.Data.ConnectionState.Open)
                {
                    await activeConnection.ExecuteAsync(
                        "EXEC sys.sp_releaseapplock @Resource = @LockResource, @LockOwner = 'Session';",
                        new { LockResource = lockResource }).ConfigureAwait(false);
                }
            }
            finally
            {
                await activeConnection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

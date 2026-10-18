using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using NUnit.Framework;

using HomeBudget.Accounting.Infrastructure.DataProtection.SqlServer;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection.SqlServer
{
    [TestFixture]
    internal sealed class SqlServerProtectionProviderTests : BaseIntegrationTests
    {
        [Test]
        public async Task CaptureAsync_WithDisposableSql2022Target_ProducesVerifiedFullDifferentialAndLogMetadata()
        {
            await using var target = await DisposableSqlServerProtectionTarget.CreateAsync(
                TestContainers,
                CancellationToken.None);
            var provider = target.CreateProvider();

            var probe = await provider.ProbeAsync(CancellationToken.None);
            probe.IsCompatible.Should().BeTrue();
            probe.ProductMajorVersion.Should().Be(16);
            probe.RecoveryModel.Should().Be("FULL");

            var full = await CaptureAsync(provider, SqlServerBackupKind.Full);
            await target.ExecuteDatabaseCommandAsync(
                "CREATE TABLE dbo.ProtectedLedger(Id int NOT NULL PRIMARY KEY, Value nvarchar(32) NOT NULL); INSERT dbo.ProtectedLedger VALUES (1, N'full');",
                CancellationToken.None);
            var differential = await CaptureAsync(provider, SqlServerBackupKind.Differential);
            await target.ExecuteDatabaseCommandAsync(
                "UPDATE dbo.ProtectedLedger SET Value = N'log-one' WHERE Id = 1;",
                CancellationToken.None);
            var firstLog = await CaptureAsync(provider, SqlServerBackupKind.Log);
            var requestedCutoff = await target.ReadServerUtcNowAsync(CancellationToken.None);
            await target.ExecuteDatabaseCommandAsync(
                "UPDATE dbo.ProtectedLedger SET Value = N'log-two' WHERE Id = 1;",
                CancellationToken.None);
            var secondLog = await CaptureAsync(provider, SqlServerBackupKind.Log);

            var chain = new[] { full.Metadata, differential.Metadata, firstLog.Metadata, secondLog.Metadata };
            provider.ValidateChain(chain);
            var restore = provider.PlanPointInTimeRecovery(requestedCutoff, chain);

            chain.Should().OnlyContain(item => item.HasBackupChecksums && item.IsVerified && !item.IsDamaged);
            restore.RequestedCutoffUtc.Should().Be(requestedCutoff);
            restore.ActualCutoffUtc.Should().Be(requestedCutoff);
            restore.StopAtUtc.Should().Be(requestedCutoff);
            restore.Backups.Should().Contain(secondLog.Metadata);
            restore.Backups.Last().LastValidRestoreTimeUtc.Should().BeAfter(requestedCutoff);
        }

        [Test]
        public async Task PlanPointInTimeRecovery_WhenRequiredLogIsMissing_RejectsTheGap()
        {
            await using var target = await DisposableSqlServerProtectionTarget.CreateAsync(
                TestContainers,
                CancellationToken.None);
            var provider = target.CreateProvider();
            var full = await CaptureAsync(provider, SqlServerBackupKind.Full);
            await target.ExecuteDatabaseCommandAsync(
                "CREATE TABLE dbo.ChainGap(Id int NOT NULL PRIMARY KEY); INSERT dbo.ChainGap VALUES (1);",
                CancellationToken.None);
            var omittedLog = await CaptureAsync(provider, SqlServerBackupKind.Log);
            var requestedCutoff = await target.ReadServerUtcNowAsync(CancellationToken.None);
            await target.ExecuteDatabaseCommandAsync(
                "INSERT dbo.ChainGap VALUES (2);",
                CancellationToken.None);
            var finalLog = await CaptureAsync(provider, SqlServerBackupKind.Log);

            Action act = () => provider.PlanPointInTimeRecovery(
                requestedCutoff,
                new[] { full.Metadata, finalLog.Metadata });

            omittedLog.Metadata.LastLsn.Should().Be(finalLog.Metadata.FirstLsn);
            act.Should().Throw<SqlServerBackupChainValidationException>()
                .WithMessage("*gap*");
        }

        [Test]
        public async Task PlanCaptureAsync_WhenExactBuildDoesNotMatch_FailsWithoutCredentialDisclosure()
        {
            await using var target = await DisposableSqlServerProtectionTarget.CreateAsync(
                TestContainers,
                CancellationToken.None);
            var provider = target.CreateProvider("16.0.0.0-not-approved");

            Func<Task> act = async () => await provider.PlanCaptureAsync(
                SqlServerBackupKind.Full,
                Guid.NewGuid(),
                CancellationToken.None);

            var exception = await act.Should().ThrowAsync<SqlServerProtectionException>();
            exception.Which.ToString().Should().NotContain(target.PasswordCanary);
            exception.Which.Message.Should().Contain("exact-product-version-mismatch");
        }

        [Test]
        public async Task ProbeAsync_WhenCancelledBeforeConnection_PropagatesCancellation()
        {
            await using var target = await DisposableSqlServerProtectionTarget.CreateAsync(
                TestContainers,
                CancellationToken.None);
            var provider = target.CreateProvider();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Func<Task> act = async () => await provider.ProbeAsync(cancellation.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        private static async Task<SqlServerCaptureResult> CaptureAsync(
            SqlServerProtectionProvider provider,
            SqlServerBackupKind kind)
        {
            var plan = await provider.PlanCaptureAsync(kind, Guid.NewGuid(), CancellationToken.None);
            return await provider.ExecuteCaptureAsync(plan, CancellationToken.None);
        }
    }
}

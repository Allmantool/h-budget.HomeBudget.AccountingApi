using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [NonParallelizable]
    public sealed class DataProtectionOperationWorkerTests : BaseIntegrationTests
    {
        [TestCase("io")]
        [TestCase("http")]
        [TestCase("provider")]
        public async Task Worker_WhenExecutorThrowsUnexpectedProviderFailure_JournalsFailureAndContinuesPolling(
            string failureKind)
        {
            var failedRequest = DataProtectionOperationTestFactory.CreateRequest(DateTime.UtcNow.AddMinutes(-1));
            var succeedingRequest = DataProtectionOperationTestFactory.CreateRequest(DateTime.UtcNow);
            var store = CreateStore();
            await store.EnqueueAsync(failedRequest, CancellationToken.None);
            await store.EnqueueAsync(succeedingRequest, CancellationToken.None);
            var executor = new FailureThenSuccessExecutor(
                failedRequest.OperationId,
                succeedingRequest.OperationId,
                CreateFailure(failureKind));

            await using var services = CreateServices(executor);
            var worker = new DataProtectionOperationWorker(
                services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new DataProtectionWorkerOptions
                {
                    IsEnabled = true,
                    PollIntervalMilliseconds = 10,
                    LeaseDurationSeconds = 5
                }),
                services.GetRequiredService<ILogger<DataProtectionOperationWorker>>());

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await worker.StartAsync(timeout.Token);
            try
            {
                await executor.SuccessfullyReconciled.WaitAsync(timeout.Token);
                await WaitForStateAsync(
                    succeedingRequest.OperationId,
                    DataProtectionOperationState.Succeeded,
                    timeout.Token);
            }
            finally
            {
                await worker.StopAsync(CancellationToken.None);
                worker.Dispose();
            }

            var failed = await CreateStore().GetAsync(failedRequest.OperationId, CancellationToken.None);
            var succeeded = await CreateStore().GetAsync(succeedingRequest.OperationId, CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(failed.State, Is.EqualTo(DataProtectionOperationState.Failed));
                Assert.That(succeeded.State, Is.EqualTo(DataProtectionOperationState.Succeeded));
                Assert.That(executor.ExecutedOperationIds, Does.Contain(failedRequest.OperationId));
                Assert.That(executor.ExecutedOperationIds, Does.Contain(succeedingRequest.OperationId));
            });
        }

        private ServiceProvider CreateServices(IDataProtectionOperationExecutor executor)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IDataProtectionOperationStore>(_ => CreateStore());
            services.AddSingleton(executor);
            services.AddSingleton<IDateTimeProvider, IntegrationDateTimeProvider>();
            services.AddScoped<DataProtectionOperationProcessor>();
            return services.BuildServiceProvider();
        }

        private SqlDataProtectionOperationStore CreateStore()
        {
            return new SqlDataProtectionOperationStore(TestContainers.AccountingDbConnectionString);
        }

        private async Task WaitForStateAsync(
            Guid operationId,
            DataProtectionOperationState expectedState,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                var operation = await CreateStore().GetAsync(operationId, cancellationToken);
                if (operation?.State == expectedState)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
            }
        }

        private static Exception CreateFailure(string failureKind)
        {
            return failureKind switch
            {
                "io" => new IOException("Synthetic independent backup provider failure."),
                "http" => new HttpRequestException("Synthetic remote provider failure."),
                "provider" => new SyntheticProviderException("Synthetic provider SDK failure."),
                _ => throw new ArgumentOutOfRangeException(nameof(failureKind))
            };
        }

        private sealed class IntegrationDateTimeProvider : IDateTimeProvider
        {
            public DateTime GetNowUtc() => DateTime.UtcNow;
        }

        private sealed class SyntheticProviderException : Exception
        {
            public SyntheticProviderException(string message)
                : base(message)
            {
            }
        }

        private sealed class FailureThenSuccessExecutor : IDataProtectionOperationExecutor
        {
            private readonly Guid _failedOperationId;
            private readonly Guid _succeedingOperationId;
            private readonly Exception _failure;
            private readonly ConcurrentQueue<Guid> _executedOperationIds = new();
            private readonly TaskCompletionSource _successfullyReconciled =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public FailureThenSuccessExecutor(
                Guid failedOperationId,
                Guid succeedingOperationId,
                Exception failure)
            {
                _failedOperationId = failedOperationId;
                _succeedingOperationId = succeedingOperationId;
                _failure = failure;
            }

            public IReadOnlyCollection<string> SupportedOperationKinds => DataProtectionOperationKinds.All;
            public Guid[] ExecutedOperationIds => _executedOperationIds.ToArray();
            public Task SuccessfullyReconciled => _successfullyReconciled.Task;

            public Task ExecuteAsync(
                DataProtectionOperation operation,
                DataProtectionOperationFenceContext fenceContext,
                CancellationToken cancellationToken)
            {
                _executedOperationIds.Enqueue(operation.OperationId);
                if (operation.OperationId == _failedOperationId)
                {
                    throw _failure;
                }

                return Task.CompletedTask;
            }

            public Task ReconcileAsync(
                DataProtectionOperation operation,
                DataProtectionOperationFenceContext fenceContext,
                CancellationToken cancellationToken)
            {
                if (operation.OperationId == _succeedingOperationId)
                {
                    _successfullyReconciled.TrySetResult();
                }

                return Task.CompletedTask;
            }
        }
    }
}

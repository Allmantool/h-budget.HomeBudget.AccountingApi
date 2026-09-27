using System;
using System.Threading;
using System.Threading.Tasks;

using MediatR;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Notifications.Models;
using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Accounts.Commands.Models;
using HomeBudget.Core.Models;

using INotificationPublisher = HomeBudget.Accounting.Notifications.Services.INotificationPublisher;

namespace HomeBudget.Components.Accounts.Commands.Handlers
{
    internal class UpdatePaymentAccountBalanceCommandHandler(
        IPaymentAccountDocumentClient paymentAccountDocumentClient,
        INotificationPublisher notificationPublisher,
        ILogger<UpdatePaymentAccountBalanceCommandHandler> logger)
        : IRequestHandler<UpdatePaymentAccountBalanceCommand, Result<Guid>>
    {
        private static readonly TimeSpan NotificationPublishTimeout = TimeSpan.FromSeconds(1);

        public async Task<Result<Guid>> Handle(
            UpdatePaymentAccountBalanceCommand request,
            CancellationToken cancellationToken)
        {
            var updateResult = request.ProjectionFence > 0
                ? await paymentAccountDocumentClient.UpdateBalanceIfNewerAsync(
                    request.PaymentAccountId,
                    request.Balance,
                    request.ProjectionFence,
                    cancellationToken)
                : await paymentAccountDocumentClient.UpdateBalanceAsync(
                    request.PaymentAccountId,
                    request.Balance,
                    cancellationToken);
            if (!updateResult.IsSucceeded)
            {
                return updateResult;
            }

            try
            {
                await notificationPublisher.PublishAsync(
                    new PaymentAccountNotification(
                        Guid.NewGuid().ToString("N"),
                        nameof(UpdatePaymentAccountBalanceCommand),
                        request.PaymentAccountId
                    )
                ).WaitAsync(NotificationPublishTimeout, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Payment account balance was updated, but notification publishing failed for account '{PaymentAccountId}'.",
                    request.PaymentAccountId);
            }

            return Result<Guid>.Succeeded(updateResult.Payload);
        }
    }
}

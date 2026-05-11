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
            var paymentAccountDocumentResult = await paymentAccountDocumentClient.GetByIdAsync(request.PaymentAccountId.ToString());

            if (!paymentAccountDocumentResult.IsSucceeded)
            {
                return Result<Guid>.Failure($"Payment account: {request.PaymentAccountId} is null");
            }

            var document = paymentAccountDocumentResult.Payload;

            if (document == null)
            {
                return Result<Guid>.Failure($"Document for account: {request.PaymentAccountId} is null");
            }

            var paymentAccountForUpdate = document.Payload;

            paymentAccountForUpdate.Balance = request.Balance;

            var updateResult = await paymentAccountDocumentClient.UpdateAsync(request.PaymentAccountId.ToString(), paymentAccountForUpdate);

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

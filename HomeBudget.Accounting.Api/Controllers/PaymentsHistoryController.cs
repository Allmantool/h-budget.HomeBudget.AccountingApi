using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using AutoMapper;
using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Models.History;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Clients.Interfaces;

namespace HomeBudget.Accounting.Api.Controllers
{
    [Route(Endpoints.PaymentsHistoryByPaymentAccountId, Name = Endpoints.PaymentsHistory)]
    [ApiController]
    public class PaymentsHistoryController(IPaymentsHistoryDocumentsClient paymentsHistoryDocumentsClient, IMapper mapper) : ControllerBase
    {
        [HttpGet]
        public async Task<Result<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>>> GetHistoryPaymentOperationsAsync(string paymentAccountId)
        {
            var documents = await paymentsHistoryDocumentsClient.GetAsync(Guid.Parse(paymentAccountId));

            var paymentAccountOperations = documents
                .Select(d => d.Payload)
                .OrderBy(op => op.Record.OperationDay)
                .ThenBy(op => op.Record.OperationUnixTime)
                .ToList();

            return Result<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>>.Succeeded(mapper.Map<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>>(paymentAccountOperations));
        }

        [HttpGet("byId/{operationId}")]
        public async Task<Result<PaymentOperationHistoryRecordResponse>> GetOperationByIdAsync(string paymentAccountId, string operationId)
        {
            if (!Guid.TryParse(paymentAccountId, out var targetAccountGuid))
            {
                return Result<PaymentOperationHistoryRecordResponse>.Failure($"Invalid payment account '{nameof(targetAccountGuid)}' has been provided");
            }

            if (!Guid.TryParse(operationId, out var targetOperationGuid))
            {
                return Result<PaymentOperationHistoryRecordResponse>.Failure($"Invalid payment operation '{nameof(targetOperationGuid)}' has been provided");
            }

            var document = await paymentsHistoryDocumentsClient.GetByIdAsync(targetAccountGuid, targetOperationGuid);

            var operationById = document.Payload;

            return operationById == null
                ? Result<PaymentOperationHistoryRecordResponse>.Failure($"The operation with '{operationId}' hasn't been found")
                : Result<PaymentOperationHistoryRecordResponse>.Succeeded(mapper.Map<PaymentOperationHistoryRecordResponse>(operationById));
        }
    }
}

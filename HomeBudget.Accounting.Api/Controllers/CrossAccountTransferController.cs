using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.Operations.Responses;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.Controllers
{
    [Route(Endpoints.CrossAccountsTransfer, Name = Endpoints.CrossAccountsTransfer)]
    [ApiController]
    public class CrossAccountTransferController(
        IMapper mapper,
        ICrossAccountsTransferService crossAccountsTransferService) : ControllerBase
    {
        [HttpPost]
        public async Task<Result<CrossAccountsTransferResponse>> ApplyAsync(
            CrossAccountsTransferRequest request,
            CancellationToken token = default)
        {
            var operationPayload = mapper.Map<CrossAccountsTransferPayload>(request);

            var responseResult = await crossAccountsTransferService.ApplyAsync(operationPayload, token);

            var response = new CrossAccountsTransferResponse
            {
                PaymentOperationId = responseResult.Payload,
                PaymentAccountIds =
                [
                    request.Sender,
                    request.Recipient
                ]
            };

            return Result<CrossAccountsTransferResponse>.Succeeded(response);
        }

        [HttpDelete]
        public async Task<Result<CrossAccountsTransferResponse>> RemoveAsync(
            RemoveTransferRequest request,
            CancellationToken token = default)
        {
            var removeTransferPayload = mapper.Map<RemoveTransferPayload>(request);

            var responseResult = await crossAccountsTransferService.RemoveAsync(removeTransferPayload, token);

            var response = new CrossAccountsTransferResponse
            {
                PaymentOperationId = removeTransferPayload.TransferOperationId,
                PaymentAccountIds = responseResult.Payload
            };

            return Result<CrossAccountsTransferResponse>.Succeeded(response);
        }
    }
}
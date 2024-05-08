using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.Operations.Responses;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

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
                PaymentOperationId = responseResult.Payload.ToString(),
                PaymentAccountIds = new[] { request.Sender, request.Recipient }
            };

            return new Result<CrossAccountsTransferResponse>(response, isSucceeded: responseResult.IsSucceeded);
        }
    }
}
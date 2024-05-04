using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Transfers.Commands.Models;

namespace HomeBudget.Components.Transfers.Commands.Handlers
{
    internal class ApplyTransferCommandHandler : IRequestHandler<ApplyTransferCommand, Result<Guid>>
    {
        public Task<Result<Guid>> Handle(ApplyTransferCommand request, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}

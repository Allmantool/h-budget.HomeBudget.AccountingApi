﻿using System;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using MediatR;

using HomeBudget.Accounting.Domain.Handlers;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Handlers;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal class RemovePaymentOperationCommandHandler(
        IMapper mapper,
        IPaymentOperationsDeliveryHandler operationsDeliveryHandler,
        IFireAndForgetHandler<IKafkaProducer<string, string>> fireAndForgetHandler)
        : BasePaymentCommandHandler(
                mapper,
                operationsDeliveryHandler,
                fireAndForgetHandler),
            IRequestHandler<RemovePaymentOperationCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(RemovePaymentOperationCommand request, CancellationToken cancellationToken)
        {
            return await HandleAsync(request, cancellationToken);
        }
    }
}
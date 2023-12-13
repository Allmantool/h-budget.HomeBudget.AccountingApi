using AutoMapper;

using HomeBudget.Components.Operations.CQRS.Commands.Models;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.MapperProfileConfigurations
{
    internal class PaymentOperationEventMappingProfile : Profile
    {
        public PaymentOperationEventMappingProfile()
        {
            CreateMap<SavePaymentOperationCommand, PaymentOperationEvent>()
                .ForMember(dest => dest.OperationUnixTime, opt => opt.MapFrom(src => src.NewOperation.OperationUnixTime))
                .ForMember(dest => dest.PaymentOperationId, opt => opt.MapFrom(src => src.NewOperation.Key))
                .ForMember(dest => dest.Payload, opt => opt.MapFrom(src => src.NewOperation));
        }
    }
}

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
                .ForMember(dest => dest.Payload, opt => opt.MapFrom(src => src.OperationForAdd))
                .ForMember(dest => dest.EventType, opt => opt.MapFrom(src => EventTypes.Add));

            CreateMap<RemovePaymentOperationCommand, PaymentOperationEvent>()
                .ForMember(dest => dest.Payload, opt => opt.MapFrom(src => src.OperationForDelete))
                .ForMember(dest => dest.EventType, opt => opt.MapFrom(src => EventTypes.Remove));

            CreateMap<UpdatePaymentOperationCommand, PaymentOperationEvent>()
                .ForMember(dest => dest.Payload, opt => opt.MapFrom(src => src.OperationForUpdate))
                .ForMember(dest => dest.EventType, opt => opt.MapFrom(src => EventTypes.Update));
        }
    }
}

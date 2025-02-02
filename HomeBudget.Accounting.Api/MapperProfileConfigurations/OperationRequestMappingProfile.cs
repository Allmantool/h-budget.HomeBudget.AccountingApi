using AutoMapper;

using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Accounting.Api.MapperProfileConfigurations
{
    internal class OperationRequestMappingProfile : Profile
    {
        public OperationRequestMappingProfile()
        {
            CreateMap<CreateOperationRequest, PaymentOperationPayload>()
                .ForMember(dest => dest.ScopeOperationId, opt => opt.MapFrom(src => src.ScopeOperationId))
                .ForMember(dest => dest.Amount, opt => opt.MapFrom(src => src.Amount))
                .ForMember(dest => dest.CategoryId, opt => opt.MapFrom(src => src.CategoryId))
                .ForMember(dest => dest.Comment, opt => opt.MapFrom(src => src.Comment))
                .ForMember(dest => dest.ContractorId, opt => opt.MapFrom(src => src.ContractorId))
                .ForMember(dest => dest.OperationDate, opt => opt.MapFrom(src => src.OperationDate));

            CreateMap<UpdateOperationRequest, PaymentOperationPayload>()
                .ForMember(dest => dest.ScopeOperationId, opt => opt.MapFrom(src => src.ScopeOperationId))
                .ForMember(dest => dest.Amount, opt => opt.MapFrom(src => src.Amount))
                .ForMember(dest => dest.CategoryId, opt => opt.MapFrom(src => src.CategoryId))
                .ForMember(dest => dest.Comment, opt => opt.MapFrom(src => src.Comment))
                .ForMember(dest => dest.ContractorId, opt => opt.MapFrom(src => src.ContractorId))
                .ForMember(dest => dest.OperationDate, opt => opt.MapFrom(src => src.OperationDate));
        }
    }
}

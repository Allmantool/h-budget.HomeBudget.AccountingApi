using AutoMapper;

using HomeBudget.Accounting.Api.Models.PaymentAccount;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Api.MapperProfileConfigurations
{
    internal class PaymentAccountMappingProfiler : Profile
    {
        public PaymentAccountMappingProfiler()
        {
            CreateMap<PaymentAccount, PaymentAccountResponse>()
                .ForMember(dest => dest.AccountType, opt => opt.MapFrom(src => src.Type.Id));
        }
    }
}

using AutoMapper;

using HomeBudget.Accounting.Api.Models.History;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Api.MapperProfileConfigurations
{
    internal class PaymentHistoryMappingProfiler : Profile
    {
        public PaymentHistoryMappingProfiler()
        {
            CreateMap<FinancialTransaction, HistoryOperationRecordResponse>()
                .ForMember(dest => dest.TransactionType, opt => opt.MapFrom(src => src.TransactionType.Id));

            CreateMap<PaymentOperationHistoryRecord, PaymentOperationHistoryRecordResponse>();
        }
    }
}

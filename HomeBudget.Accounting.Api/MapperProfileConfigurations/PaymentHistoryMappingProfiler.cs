using AutoMapper;

using HomeBudget.Accounting.Api.Models.History;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Api.MapperProfileConfigurations
{
    internal class PaymentHistoryMappingProfiler : Profile
    {
        public PaymentHistoryMappingProfiler()
        {
            CreateMap<FinancialTransaction, HistoryOperationRecordResponse>()
                .ForMember(dest => dest.TransactionType, opt => opt.MapFrom(src => src.TransactionType.Key))
                .ForMember(
                    dest => dest.RelatedPaymentAccountId,
                    opt => opt.MapFrom(src =>
                        src.TransactionType.Key == TransactionTypes.Transfer.Key ? src.ContractorId : (System.Guid?)null));

            CreateMap<PaymentOperationHistoryRecord, PaymentOperationHistoryRecordResponse>();
        }
    }
}

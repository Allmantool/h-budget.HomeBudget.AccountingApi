using AutoMapper;

using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Accounting.Api.MapperProfileConfigurations
{
    internal class CrossAccountsTransferRequestMappingProfiler : Profile
    {
        public CrossAccountsTransferRequestMappingProfiler()
        {
            CreateMap<CrossAccountsTransferRequest, CrossAccountsTransferPayload>();
        }
    }
}

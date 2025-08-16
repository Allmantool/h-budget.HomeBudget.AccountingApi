using AutoMapper;

using HomeBudget.Accounting.Api.Models.Category;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Api.MapperProfileConfigurations
{
    public class CategoryMappingProfiler : Profile
    {
        public CategoryMappingProfiler()
        {
            CreateMap<Category, CategoryResponse>()
                .ForMember(dest => dest.CategoryType, opt => opt.MapFrom(src => src.CategoryType.Key));
        }
    }
}

using System.Reflection;

namespace HomeBudget.Components.Operations.MapperProfileConfigurations
{
    public static class PaymentOperationsComponentMappingProfile
    {
        public static Assembly GetExecutingAssembly() => typeof(PaymentOperationsComponentMappingProfile).Assembly;
    }
}

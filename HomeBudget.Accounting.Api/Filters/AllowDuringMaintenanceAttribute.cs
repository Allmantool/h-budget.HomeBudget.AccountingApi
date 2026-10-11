using System;

namespace HomeBudget.Accounting.Api.Filters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    internal sealed class AllowDuringMaintenanceAttribute : Attribute
    {
    }
}

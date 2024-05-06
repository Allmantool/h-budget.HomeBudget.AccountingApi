using System;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Domain.Services
{
    public interface IFireAndForgetHandler<out T>
    {
        void Execute(Func<T, Task> callback);
    }
}

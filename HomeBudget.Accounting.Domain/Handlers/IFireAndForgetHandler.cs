using System;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Domain.Handlers
{
    public interface IFireAndForgetHandler<out T>
    {
        void Execute(Func<T, Task> callback);
    }
}

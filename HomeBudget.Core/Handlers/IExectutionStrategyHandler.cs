using System;
using System.Threading.Tasks;

namespace HomeBudget.Core.Handlers
{
    public interface IExectutionStrategyHandler<out T>
    {
        void ExecuteFireAndForget(Func<T, Task> callback);

        Task ExecuteAndWaitAsync(Func<T, Task> callback);
    }
}

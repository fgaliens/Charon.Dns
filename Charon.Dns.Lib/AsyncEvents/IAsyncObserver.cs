using System.Threading.Tasks;

namespace Charon.Dns.Lib.AsyncEvents;

public interface IAsyncObserver<in T>
{
    Task OnEvent(T eventArgs);
    Task OnCompleted();
}
using System.Threading.Tasks;

namespace Charon.Dns.Lib.AsyncEvents;

public interface IAsyncEventEmitter<in T>
{
    Task SendEvent(T eventArgs);
}

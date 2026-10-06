using System.Net;

namespace Charon.Dns.AccessControl;

public interface IUserAccessControlManager
{
    Task UnblockUser(IPAddress ip);
    Task EvaluateAndEnforce();
    Task RevertAllTaggedRules();
}

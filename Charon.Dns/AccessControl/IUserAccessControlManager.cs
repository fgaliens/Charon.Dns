namespace Charon.Dns.AccessControl;

public interface IUserAccessControlManager
{
    Task EvaluateAndEnforce();
    Task RevertAllTaggedRules();
}

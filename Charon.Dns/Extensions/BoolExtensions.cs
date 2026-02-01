namespace Charon.Dns.Extensions;

public static class BoolExtensions
{
    public static bool SwitchToFalseIfTrue(this ref bool value)
    {
        if (!value)
        {
            return false;
        }

        value = false;
        return true;

    }
}
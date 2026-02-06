using System.Net;

namespace InfernalHierarchy.Host.Security;

internal static class LoopbackGuard
{
    internal static bool IsLoopback(IPAddress? ip)
        => ip != null && (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.IPv6Loopback));
}

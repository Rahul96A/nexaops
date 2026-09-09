namespace NexaOps.Api.Identity;

/// <summary>
/// Names shared between the authentication schemes.
/// <para>
/// NexaOps accepts two kinds of credential — a person's bearer token and an integration key —
/// and the default scheme is a policy that forwards to whichever one the request presented. That
/// indirection matters because the tenant middleware runs immediately after authentication and
/// reads the tenant from the principal: a credential authenticated later, inside an
/// authorization filter, would reach the application with no tenant scope.
/// </para>
/// </summary>
public static class NexaOpsAuthentication
{
    /// <summary>The policy scheme that picks a real scheme per request.</summary>
    public const string DefaultScheme = "NexaOps";

    /// <summary>
    /// Where an integration key is presented.
    /// <para>
    /// A header, never a query string: query strings are written to access logs, proxy logs and
    /// browser history, and a credential that ends up in a log is a credential that has leaked.
    /// </para>
    /// </summary>
    public const string KeyHeader = "X-NexaOps-Key";
}

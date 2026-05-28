namespace Ez.Handball.Infrastructure.TableAccess;

internal static class ODataFilter
{
    // OData string literals escape a single quote by doubling it.
    internal static string Escape(string value) => value.Replace("'", "''");
}

using Interact.PartnerApi;

/// <summary>
/// Creates (or updates) a tenant with EU compliance and GPU support, leaving out whichever your
/// plan doesn't include. Interact sets your plan; which tenants get what in it is up to you.
/// </summary>
internal static class TenantSetup
{
    public static async Task<Tenant> PutAsync(PartnerApiClient client, string tenantRef, string name)
    {
        List<string> modules = ["simulations", "compliance"];
        List<string> flags = ["gpu_support"];
        while (true)
        {
            try
            {
                // No flags at all, rather than none, keeps whatever the tenant has.
                return await client.PutTenantAsync(tenantRef, new TenantRequest(name, modules,
                                                                                  Flags: flags.Count > 0 ? flags : null));
            }
            catch (PartnerApiException refused) when (refused.Code == "validation_failed")
            {
                var fields = refused.Errors.Select(error => error.Pointer).ToHashSet();
                if (fields.Contains("/modules") && modules.Remove("compliance"))
                {
                    Console.WriteLine("   Your plan doesn't include EU compliance: left out.");
                }
                else if (fields.Contains("/flags") && flags.Count > 0)
                {
                    flags.Clear();
                    Console.WriteLine("   Your plan doesn't include GPU support: left out.");
                }
                else
                {
                    throw;
                }
            }
        }
    }
}

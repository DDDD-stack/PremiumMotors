namespace WEBTechnologies_Final.Services
{
    public static class RateLimitPolicies
    {
        /// <summary>Tight window for credential endpoints (login, register, refresh, reset).</summary>
        public const string Auth = "auth";
    }
}

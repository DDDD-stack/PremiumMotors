namespace WEBTechnologies_Final.Services
{
    // Keys for the MVC website's server-side session. The mobile app does not use sessions -
    // it carries the same identity as JWT claims (see TokenService).
    public static class SessionKeys
    {
        public const string IsAdmin = "IsAdmin";
        public const string Username = "Username";
        public const string UserId = "UserId";

        // Seller is a capability on the account, not a role, so it rides alongside IsAdmin
        // rather than replacing it. Re-written on login and whenever the user opts in.
        public const string IsSeller = "IsSeller";

        // Whether the seller is a dealership rather than a private seller. Carried in the
        // session purely so the layout can decide whether to offer "Register as a business"
        // without a database round trip on every single page render.
        public const string IsDealer = "IsDealer";
    }
}

namespace WEBTechnologies_Final.Services
{
    /// <summary>
    /// Which version of the terms and privacy policy is currently published.
    ///
    /// A date rather than a number, because that is what somebody comparing two versions
    /// actually wants to know, and because it cannot be forgotten to increment in the way a
    /// number can.
    ///
    /// BUMP THIS EVERY TIME THE TERMS OR THE PRIVACY POLICY CHANGE IN SUBSTANCE. Existing
    /// users keep the version they accepted; the difference between what they agreed to and
    /// what is published today is exactly the thing a re-acceptance prompt would need, and it
    /// is unanswerable if this never moves.
    ///
    /// Typo fixes do not count. A change to what anyone is agreeing to does.
    /// </summary>
    public static class LegalDocuments
    {
        public const string Version = "2026-09-05";
    }
}

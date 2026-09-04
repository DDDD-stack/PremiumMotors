namespace WEBTechnologies_Final.Services
{
    /// <summary>
    /// How large a listing form is allowed to be.
    ///
    /// Kestrel's default MaxRequestBodySize is 30 MB, which is the real reason multi-photo
    /// uploads failed from a phone: Storage:MaxFilesPerListing allows 12 photos at
    /// Storage:MaxFileBytes (8 MB) each, and a modern phone camera produces 3-8 MB per shot.
    /// Six photos straight from a camera roll therefore exceeded the transport limit and were
    /// rejected with a bare 413 before a single line of validation ran - the upload appeared
    /// to fail for no reason.
    ///
    /// This ceiling matches the storage policy (12 x 8 MB) plus room for the rest of the form.
    /// It is a TRANSPORT limit only: per-file type and size checks still happen in
    /// ImageValidator, and the per-listing count is still enforced by IPhotoStorage. Raising
    /// it does not widen what is accepted, only what is allowed to arrive far enough to be
    /// rejected properly.
    ///
    /// In practice payloads are far smaller: site.js downscales images in the browser before
    /// they are sent. This limit is the floor under that, for browsers where it cannot run.
    /// </summary>
    public static class UploadLimits
    {
        public const long ListingFormBytes = 100L * 1024 * 1024;

        /// <summary>One picture, so the listing ceiling would be absurd here.</summary>
        public const long AvatarBytes = 12L * 1024 * 1024;
    }
}

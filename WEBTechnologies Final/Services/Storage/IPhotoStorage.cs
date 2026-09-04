namespace WEBTechnologies_Final.Services.Storage
{
    public record PhotoSaveResult(IReadOnlyList<string> Paths, IReadOnlyList<string> Errors);

    /// <summary>
    /// Where car photos live. Local disk is development-only: on a cloud host the filesystem is
    /// ephemeral and not shared between instances, so uploads would vanish on every deploy.
    /// </summary>
    public interface IPhotoStorage
    {
        Task<PhotoSaveResult> SaveAsync(IEnumerable<IFormFile>? files, CancellationToken ct = default);

        /// <summary>
        /// Removes a stored photo. Best-effort by design: the caller has already dropped the
        /// path from the listing, and an orphaned blob is a cleanup problem, not a user-facing
        /// failure. Never throws for a file that is already gone.
        /// </summary>
        Task DeleteAsync(string path, CancellationToken ct = default);
    }
}

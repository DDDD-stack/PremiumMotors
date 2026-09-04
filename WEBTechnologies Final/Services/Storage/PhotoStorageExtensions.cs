namespace WEBTechnologies_Final.Services.Storage
{
    public static class PhotoStorageExtensions
    {
        /// <summary>
        /// Removes every blob belonging to a listing that has just been deleted.
        ///
        /// ORDER MATTERS, and only one order is safe: delete the database row FIRST, then call
        /// this. If a blob delete fails afterwards you are left with an orphan, which costs
        /// storage and nothing else. Do it the other way round and a failed row delete leaves a
        /// live listing whose photos have all 404'd - visibly broken to every visitor.
        ///
        /// Best-effort, like <see cref="IPhotoStorage.DeleteAsync"/> itself: the listing is
        /// already gone, so a failure here is a cleanup problem and must never surface as a
        /// user-facing error.
        /// </summary>
        public static async Task DeleteAllAsync(
            this IPhotoStorage storage, IEnumerable<string>? paths, CancellationToken ct = default)
        {
            if (paths is null) return;

            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                await storage.DeleteAsync(path, ct);
            }
        }
    }
}

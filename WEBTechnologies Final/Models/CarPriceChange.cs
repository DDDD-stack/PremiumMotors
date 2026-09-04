namespace WEBTechnologies_Final.Models
{
    /// <summary>
    /// One row per price change on a listing, written by the edit paths.
    ///
    /// Cheap to keep and impossible to reconstruct later: without it "reduced from EUR 12,500"
    /// and any price-drop alert are simply unbuildable, because the old price is overwritten
    /// the moment the seller saves.
    /// </summary>
    public class CarPriceChange
    {
        public int Id { get; set; }
        public int CarId { get; set; }

        /// <summary>The price AFTER this change.</summary>
        public decimal Price { get; set; }

        /// <summary>Null for the first row, which records the price the listing opened at.</summary>
        public decimal? PreviousPrice { get; set; }

        public DateTime ChangedUtc { get; set; } = DateTime.UtcNow;

        public Car? Car { get; set; }
    }
}

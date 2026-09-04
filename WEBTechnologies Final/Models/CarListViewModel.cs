using Microsoft.AspNetCore.Mvc.Rendering;

namespace WEBTechnologies_Final.Models
{
    public class CarListViewModel
    {
        public IReadOnlyList<Car> Cars { get; set; } = new List<Car>();

        public string? Search { get; set; }
        public CarType? Type { get; set; }
        public string? Make { get; set; }
        public string? Model { get; set; }
        public int? Year { get; set; }

        // Marketplace filters. Buyers shop by budget and mileage far more than by body type,
        // so these three carry most of the traffic on a used-car site.
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public int? MaxMileage { get; set; }
        public FuelType? Fuel { get; set; }
        public TransmissionType? Gearbox { get; set; }

        public string SortBy { get; set; } = "newest";

        // Paging: the browse page used to materialize every published listing on every request.
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 24;
        public int TotalCount { get; set; }

        public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        public bool HasPreviousPage => Page > 1;
        public bool HasNextPage => Page < TotalPages;

        public SelectList? TypeOptions { get; set; }
        public SelectList? MakeOptions { get; set; }
        public SelectList? ModelOptions { get; set; }
        public SelectList? YearOptions { get; set; }
        public SelectList? SortOptions { get; set; }
        public SelectList? FuelOptions { get; set; }
        public SelectList? GearboxOptions { get; set; }

        public bool HasActiveFilters =>
            !string.IsNullOrWhiteSpace(Search) ||
            Type.HasValue ||
            !string.IsNullOrWhiteSpace(Make) ||
            !string.IsNullOrWhiteSpace(Model) ||
            Year.HasValue ||
            MinPrice.HasValue ||
            MaxPrice.HasValue ||
            MaxMileage.HasValue ||
            Fuel.HasValue ||
            Gearbox.HasValue;

        public int ActiveFilterCount =>
            (string.IsNullOrWhiteSpace(Search) ? 0 : 1) +
            (Type.HasValue ? 1 : 0) +
            (string.IsNullOrWhiteSpace(Make) ? 0 : 1) +
            (string.IsNullOrWhiteSpace(Model) ? 0 : 1) +
            (Year.HasValue ? 1 : 0) +
            (MinPrice.HasValue || MaxPrice.HasValue ? 1 : 0) +
            (MaxMileage.HasValue ? 1 : 0) +
            (Fuel.HasValue ? 1 : 0) +
            (Gearbox.HasValue ? 1 : 0);
    }
}

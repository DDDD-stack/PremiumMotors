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

        public string SortBy { get; set; } = "newest";

        public SelectList? TypeOptions { get; set; }
        public SelectList? MakeOptions { get; set; }
        public SelectList? ModelOptions { get; set; }
        public SelectList? YearOptions { get; set; }
        public SelectList? SortOptions { get; set; }

        public bool HasActiveFilters =>
            !string.IsNullOrWhiteSpace(Search) ||
            Type.HasValue ||
            !string.IsNullOrWhiteSpace(Make) ||
            !string.IsNullOrWhiteSpace(Model) ||
            Year.HasValue;
    }
}

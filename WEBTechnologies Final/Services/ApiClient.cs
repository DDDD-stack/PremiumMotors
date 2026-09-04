using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services.Storage;

namespace WEBTechnologies_Final.Services
{
    /// <summary>
    /// Data-access helper shared by the MVC controllers. Despite the name it talks to the
    /// database directly rather than over HTTP.
    ///
    /// Account creation and password checking used to live here and wrote plaintext passwords;
    /// that responsibility now belongs to <see cref="Auth.AccountService"/>, which the website
    /// and the mobile API both use.
    /// </summary>
    public class ApiClient
    {
        private readonly AppDbContext _context;
        private readonly IPhotoStorage _photos;

        public ApiClient(AppDbContext context, IPhotoStorage photos)
        {
            _context = context;
            _photos = photos;
        }

        public async Task<List<Car>> GetCarsAsync(
            string? search = null, string? type = null, string? make = null,
            string? model = null, int? year = null, string? sortBy = null)
        {
            var query = _context.Cars.Include(c => c.Offers).AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(c => c.Make.Contains(search) || c.Model.Contains(search) || c.Description.Contains(search));
            }
            if (!string.IsNullOrEmpty(type) && Enum.TryParse<CarType>(type, true, out var parsedType))
            {
                query = query.Where(c => c.Type == parsedType);
            }
            if (!string.IsNullOrEmpty(make))
            {
                query = query.Where(c => c.Make == make);
            }
            if (!string.IsNullOrEmpty(model))
            {
                query = query.Where(c => c.Model == model);
            }
            if (year.HasValue)
            {
                query = query.Where(c => c.Year == year.Value);
            }

            query = sortBy switch
            {
                "price_asc" => query.OrderBy(c => c.Price),
                "price_desc" => query.OrderByDescending(c => c.Price),
                "year_asc" => query.OrderBy(c => c.Year),
                "year_desc" => query.OrderByDescending(c => c.Year),
                _ => query.OrderByDescending(c => c.Id)
            };

            return await query.ToListAsync();
        }

        public async Task<Car?> GetCarAsync(int id)
        {
            return await _context.Cars
                .Include(c => c.Offers)
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<List<string>> GetMakesAsync() =>
            await _context.Cars.Select(c => c.Make).Distinct().OrderBy(m => m).ToListAsync();

        public async Task<List<string>> GetModelsAsync(string? make = null)
        {
            var query = _context.Cars.AsQueryable();
            if (make is not null)
            {
                query = query.Where(c => c.Make == make);
            }
            return await query.Select(c => c.Model).Distinct().OrderBy(m => m).ToListAsync();
        }

        public async Task<List<int>> GetYearsAsync() =>
            await _context.Cars.Select(c => c.Year).Distinct().OrderByDescending(y => y).ToListAsync();

        public async Task<Car?> CreateCarAsync(Car car)
        {
            _context.Cars.Add(car);
            await _context.SaveChangesAsync();
            return car;
        }

        /// <summary>
        /// Applies an edit to a listing. Only the fields the edit form actually owns are
        /// assigned.
        ///
        /// This used to call CurrentValues.SetValues(car), which copied EVERY column from a
        /// partially-bound model - so editing a description silently unpublished the listing,
        /// cleared its owner, erased any recorded sale, reset ClosureProcessed and CreatedUtc,
        /// and dropped its photos. Never reintroduce SetValues here.
        /// </summary>
        /// <param name="newImagePaths">Photos to APPEND.</param>
        /// <param name="removeImagePaths">
        /// Photos to drop. Passed explicitly rather than inferred from a full replacement list,
        /// so an edit that uploads nothing still keeps every existing photo.
        /// </param>
        public async Task<Car?> UpdateCarAsync(
            Car car,
            IReadOnlyList<string>? newImagePaths = null,
            IReadOnlyList<string>? removeImagePaths = null)
        {
            var existing = await _context.Cars.FirstOrDefaultAsync(c => c.Id == car.Id);
            if (existing is null) return null;

            existing.Make = car.Make;
            existing.Model = car.Model;
            existing.Type = car.Type;
            existing.Year = car.Year;
            existing.Description = car.Description;
            // Price changes are recorded centrally in AppDbContext.SaveChangesAsync, so every
            // edit path is covered rather than just this one.
            existing.Price = car.Price;
            existing.Country = car.Country;
            existing.City = car.City;

            // Vehicle specification.
            existing.Mileage = car.Mileage;
            existing.ServiceHistory = car.ServiceHistory;
            existing.ServiceHistoryNotes = car.ServiceHistoryNotes;
            existing.FuelType = car.FuelType;
            existing.Transmission = car.Transmission;
            existing.Drivetrain = car.Drivetrain;
            existing.EngineSizeCc = car.EngineSizeCc;
            existing.PowerHp = car.PowerHp;
            existing.Doors = car.Doors;
            existing.Seats = car.Seats;
            existing.ExteriorColour = car.ExteriorColour;
            existing.PreviousOwners = car.PreviousOwners;
            existing.Condition = car.Condition;
            existing.HasAccidentHistory = car.HasAccidentHistory;
            existing.Vin = car.Vin;
            existing.FirstRegistration = car.FirstRegistration;

            // Reassign rather than mutate: ImagePaths goes through a JSON value converter, so an
            // in-place Add or Remove is not detected as a change.
            var photos = existing.ImagePaths.ToList();

            if (removeImagePaths is { Count: > 0 })
                photos = photos.Where(p => !removeImagePaths.Contains(p)).ToList();

            if (newImagePaths is { Count: > 0 })
                photos.AddRange(newImagePaths);

            existing.ImagePaths = photos;

            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> DeleteCarAsync(int id)
        {
            var car = await _context.Cars.FirstOrDefaultAsync(c => c.Id == id);
            if (car is null) return false;

            // Read the paths before the entity goes: after SaveChanges the row is gone and
            // nothing else records where the blobs were. Deleting a listing used to leave
            // every one of its photos on disk (or in the bucket) forever - editing a listing
            // cleaned up removed photos correctly, deleting the whole listing did not.
            var photos = car.ImagePaths.ToList();

            _context.Cars.Remove(car);
            await _context.SaveChangesAsync();

            await _photos.DeleteAllAsync(photos);
            return true;
        }

        /// <summary>Takes a listing off the market without deleting it or its offer history.</summary>
        public async Task<bool> ArchiveCarAsync(int id)
        {
            var car = await _context.Cars.FirstOrDefaultAsync(c => c.Id == id);
            if (car is null) return false;

            car.Status = ListingStatus.Archived;
            await _context.SaveChangesAsync();
            return true;
        }

        /// <summary>Puts an archived or draft listing back on the market.</summary>
        public async Task<bool> PublishCarAsync(int id)
        {
            var car = await _context.Cars.FirstOrDefaultAsync(c => c.Id == id);
            if (car is null) return false;

            car.Status = ListingStatus.Active;
            car.PublishedUtc ??= DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<CarStats?> GetStatsAsync()
        {
            var total = await _context.Cars.CountAsync();
            var active = await _context.Cars.CountAsync(c => c.Status == ListingStatus.Active);
            var sold = await _context.Cars.CountAsync(c => c.Status == ListingStatus.Sold);
            var reserved = await _context.Cars.CountAsync(c => c.Status == ListingStatus.Reserved);
            var totalOffers = await _context.Offers.CountAsync();
            var pendingOffers = await _context.Offers.CountAsync(o => o.Status == OfferStatus.Pending);

            return new CarStats(total, active, reserved, sold, totalOffers, pendingOffers);
        }

        // ---------- Favourites (keyed by stable user id) ----------

        public async Task<List<int>> GetFavoriteIdsAsync(int userId) =>
            await _context.UserFavoriteCars
                .Where(f => f.UserId == userId)
                .OrderByDescending(f => f.CreatedUtc)
                .Select(f => f.CarId)
                .ToListAsync();

        /// <summary>Every favourited listing in a single round trip.</summary>
        public async Task<List<Car>> GetFavoriteCarsAsync(int userId) =>
            await _context.UserFavoriteCars
                .Where(f => f.UserId == userId)
                .OrderByDescending(f => f.CreatedUtc)
                .Join(_context.Cars, f => f.CarId, c => c.Id, (f, c) => c)
                .ToListAsync();

        public async Task<bool> IsFavoriteAsync(int userId, int carId) =>
            await _context.UserFavoriteCars.AnyAsync(f => f.UserId == userId && f.CarId == carId);

        /// <summary>Adds or removes the favourite. Returns true if it is now a favourite.</summary>
        public async Task<bool> ToggleFavoriteAsync(int userId, int carId)
        {
            var existing = await _context.UserFavoriteCars
                .FirstOrDefaultAsync(f => f.UserId == userId && f.CarId == carId);

            if (existing is not null)
            {
                _context.UserFavoriteCars.Remove(existing);
                await _context.SaveChangesAsync();
                return false;
            }

            _context.UserFavoriteCars.Add(new UserFavoriteCar
            {
                UserId = userId,
                CarId = carId,
                CreatedUtc = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            return true;
        }
    }

    public record CarStats(
        int Total, int Active, int Reserved, int Sold, int TotalOffers, int PendingOffers);
}

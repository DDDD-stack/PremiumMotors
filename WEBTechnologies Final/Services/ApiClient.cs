using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Services
{

    public class ApiClient
    {
        private readonly AppDbContext _context;

        public ApiClient(HttpClient http, AppDbContext context)
        {

            _context = context;
        }

        public async Task<List<Car>> GetCarsAsync(
            string? search = null, string? type = null, string? make = null,
            string? model = null, int? year = null, string? sortBy = null)
        {
            var query = _context.Cars.Include(c => c.Bids).AsQueryable();

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
                "price_asc" => query.OrderBy(c => c.StartingPrice),
                "price_desc" => query.OrderByDescending(c => c.StartingPrice),
                "year_asc" => query.OrderBy(c => c.Year),
                "year_desc" => query.OrderByDescending(c => c.Year),
                _ => query.OrderByDescending(c => c.Id)
            };

            return await query.ToListAsync();
        }

        public async Task<Car?> GetCarAsync(int id)
        {
            return await _context.Cars
                .Include(c => c.Bids)
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

        public async Task<Car?> UpdateCarAsync(Car car)
        {
            var existing = await _context.Cars.FirstOrDefaultAsync(c => c.Id == car.Id);
            if (existing is null) return null;

            _context.Entry(existing).CurrentValues.SetValues(car);
            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> DeleteCarAsync(int id)
        {
            var car = await _context.Cars.FirstOrDefaultAsync(c => c.Id == id);
            if (car is null) return false;

            _context.Cars.Remove(car);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<(Car? car, string? error)> PlaceBidAsync(int id, string bidderName, decimal amount)
        {
            var car = await _context.Cars.Include(c => c.Bids).FirstOrDefaultAsync(c => c.Id == id);
            if (car is null) return (null, "Car listing not found.");

            decimal currentPrice = car.Bids.Any() ? car.Bids.Max(b => b.Amount) : car.StartingPrice;
            if (amount <= currentPrice)
            {
                return (null, $"Your bid must be higher than the current price of {currentPrice:C}");
            }

            var newBid = new Bid
            {
                CarId = id,
                Amount = amount,
                BidderUsername = bidderName,
                CreatedUtc = DateTime.UtcNow
            };
            _context.Bids.Add(newBid);

            await _context.SaveChangesAsync();
            return (car, null);
        }

        public async Task<bool> CloseAuctionAsync(int id)
        {
            var car = await _context.Cars.FirstOrDefaultAsync(c => c.Id == id);
            if (car is null) return false;

            car.AuctionEnd = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<CarStats?> GetStatsAsync()
        {
            var total = await _context.Cars.CountAsync();
            var sold = await _context.Cars.CountAsync(c => c.IsSold);
            var active = total - sold;
            var totalBids = await _context.Bids.CountAsync();

            return new CarStats(total, active, sold, totalBids);
        }

        public async Task<(UserDto? user, string? error)> RegisterAsync(
            string username, string email, string phone, string password)
        {
            var normalizedUsername = username.Trim().ToLower();
            var normalizedEmail = email.Trim().ToLower();

            var exists = await _context.Users.AnyAsync(u => u.Username.ToLower() == normalizedUsername || u.Email.ToLower() == normalizedEmail);
            if (exists) return (null, "Username or Email already registered.");

            var newUser = new User
            {
                Username = username,
                Email = email,
                Phone = phone,
                PasswordHash = password
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            return (new UserDto(newUser.Id, newUser.Username, newUser.Email), null);
        }

        public async Task<(UserDto? user, string? error)> ValidateAsync(
            string username, string password)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.Trim().ToLower());
            if (user is null || user.PasswordHash != password)
            {
                return (null, "Invalid username or password.");
            }

            return (new UserDto(user.Id, user.Username, user.Email), null);
        }

        public async Task<List<int>> GetFavoriteIdsAsync(string username)
        {
            return await _context.UserFavoriteCars
                .Where(f => f.Username.ToLower() == username.Trim().ToLower())
                .Select(f => f.CarId)
                .ToListAsync();
        }

        public async Task<bool> IsFavoriteAsync(string username, int carId)
        {
            return await _context.UserFavoriteCars
                .AnyAsync(f => f.Username.ToLower() == username.Trim().ToLower() && f.CarId == carId);
        }

        public async Task<bool> ToggleFavoriteAsync(string username, int carId)
        {
            var formattedName = username.Trim();
            var existing = await _context.UserFavoriteCars
                .FirstOrDefaultAsync(f => f.Username.ToLower() == formattedName.ToLower() && f.CarId == carId);

            if (existing is not null)
            {
                _context.UserFavoriteCars.Remove(existing);
                await _context.SaveChangesAsync();
                return false;
            }

            _context.UserFavoriteCars.Add(new UserFavoriteCar { Username = formattedName, CarId = carId });
            await _context.SaveChangesAsync();
            return true;
        }
    }

    public record UserDto(int Id, string Username, string Email);
    public record CarStats(int Total, int Active, int Sold, int TotalBids);
}

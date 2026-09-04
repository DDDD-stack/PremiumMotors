using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services.Marketplace;
using Xunit;

namespace PremiumMotors.Tests;

/// <summary>
/// Price history is recorded in AppDbContext.SaveChangesAsync rather than in the edit
/// actions, because a listing can be repriced from four places and the first attempt only
/// covered one of them — a seller dropping their price recorded nothing at all. These tests
/// hold the central behaviour, and the last one covers the display bug that hid the very
/// first price drop on every listing.
/// </summary>
public class PriceHistoryTests
{
    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<(AppDbContext Db, Car Car)> ListingAsync(decimal price)
    {
        var db = NewContext();
        var car = new Car
        {
            Make = "BMW", Model = "320d", Year = 2020, Country = "Albania",
            Price = price, OwnerId = 1, Status = ListingStatus.Active
        };
        db.Cars.Add(car);
        await db.SaveChangesAsync();
        return (db, car);
    }

    [Fact]
    public async Task Creating_a_listing_records_no_history_row()
    {
        var (db, car) = await ListingAsync(20000m);

        // Deliberate: the Id does not exist until the insert completes, and the original
        // price is preserved anyway as the PreviousPrice of the first change.
        Assert.Empty(await db.CarPriceChanges.Where(p => p.CarId == car.Id).ToListAsync());
    }

    [Fact]
    public async Task Changing_the_price_records_both_the_old_and_the_new_value()
    {
        var (db, car) = await ListingAsync(20000m);

        car.Price = 17500m;
        await db.SaveChangesAsync();

        var row = Assert.Single(await db.CarPriceChanges.Where(p => p.CarId == car.Id).ToListAsync());
        Assert.Equal(17500m, row.Price);
        Assert.Equal(20000m, row.PreviousPrice);
    }

    [Fact]
    public async Task Editing_something_other_than_the_price_records_nothing()
    {
        var (db, car) = await ListingAsync(20000m);

        car.Description = "Now with a new description.";
        car.Mileage = 61000;
        await db.SaveChangesAsync();

        Assert.Empty(await db.CarPriceChanges.Where(p => p.CarId == car.Id).ToListAsync());
    }

    [Fact]
    public async Task Every_reprice_adds_a_row()
    {
        var (db, car) = await ListingAsync(20000m);

        car.Price = 18000m;
        await db.SaveChangesAsync();
        car.Price = 16250m;
        await db.SaveChangesAsync();

        var rows = await db.CarPriceChanges.Where(p => p.CarId == car.Id)
            .OrderBy(p => p.Id).ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal(20000m, rows[0].PreviousPrice);
        Assert.Equal(16250m, rows[1].Price);
    }

    [Fact]
    public async Task The_first_price_drop_is_detected_from_PreviousPrice()
    {
        // The bug this covers: the "reduced from" lookup originally took Max(Price) across
        // the change rows. After one reduction the only row's Price IS the new, lower price,
        // so the maximum was never above the current price and the badge never appeared —
        // on precisely the drop most worth advertising.
        var (db, car) = await ListingAsync(20000m);
        car.Price = 17500m;
        await db.SaveChangesAsync();

        var extras = await new ListingExtrasService(db).ForCarsAsync(new[] { car });

        Assert.True(extras.PreviousPrices.ContainsKey(car.Id));
        Assert.Equal(20000m, extras.PreviousPrices[car.Id]);
    }

    [Fact]
    public async Task A_price_that_went_up_is_not_advertised_as_a_drop()
    {
        var (db, car) = await ListingAsync(15000m);
        car.Price = 16000m;
        await db.SaveChangesAsync();

        var extras = await new ListingExtrasService(db).ForCarsAsync(new[] { car });

        Assert.False(extras.PreviousPrices.ContainsKey(car.Id));
    }
}

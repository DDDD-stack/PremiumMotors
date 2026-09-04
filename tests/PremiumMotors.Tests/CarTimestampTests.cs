using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services;
using Xunit;

namespace PremiumMotors.Tests;

/// <summary>
/// Regression cover for the admin-create crash: an &lt;input type="date"&gt; binds to a
/// DateTime with Kind=Unspecified, and Npgsql refuses to write anything but Kind=Utc to a
/// timestamptz. The normalization lives on the EF property so no write path can skip it,
/// and these tests assert that rather than trusting each controller to remember.
/// </summary>
public class CarTimestampTests
{
    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public void FirstRegistration_has_a_utc_normalizing_converter()
    {
        using var db = NewContext();

        var converter = db.Model
            .FindEntityType(typeof(Car))!
            .FindProperty(nameof(Car.FirstRegistration))!
            .GetValueConverter();

        Assert.NotNull(converter);

        // Kind=Unspecified is exactly what model binding produces from a date input.
        var typed = new DateTime(2018, 6, 15, 0, 0, 0, DateTimeKind.Unspecified);
        var stored = (DateTime?)converter!.ConvertToProvider(typed);

        Assert.NotNull(stored);
        Assert.Equal(DateTimeKind.Utc, stored!.Value.Kind);
    }

    [Fact]
    public void FirstRegistration_is_tagged_not_shifted()
    {
        using var db = NewContext();

        var converter = db.Model
            .FindEntityType(typeof(Car))!
            .FindProperty(nameof(Car.FirstRegistration))!
            .GetValueConverter()!;

        // A registration date is a calendar date, not an instant. Converting it through a
        // timezone would move a 15 June registration to 14 June for anyone east of UTC.
        AppTime.Configure("Europe/Tirane");
        var typed = new DateTime(2018, 6, 15, 0, 0, 0, DateTimeKind.Unspecified);
        var stored = (DateTime?)converter.ConvertToProvider(typed);

        Assert.Equal(new DateTime(2018, 6, 15, 0, 0, 0, DateTimeKind.Utc), stored!.Value);
    }

    [Fact]
    public void FirstRegistration_accepts_null()
    {
        using var db = NewContext();

        var converter = db.Model
            .FindEntityType(typeof(Car))!
            .FindProperty(nameof(Car.FirstRegistration))!
            .GetValueConverter()!;

        Assert.Null(converter.ConvertToProvider(null));
    }

    [Fact]
    public void Every_datetime_written_by_the_app_is_utc()
    {
        // The other timestamps are set in code from DateTime.UtcNow rather than bound from a
        // form. If one is ever changed to come from user input it must be normalized too.
        var car = new Car();
        Assert.Equal(DateTimeKind.Utc, car.CreatedUtc.Kind);

        var offer = new Offer();
        Assert.Equal(DateTimeKind.Utc, offer.CreatedUtc.Kind);

        var conversation = new Conversation();
        Assert.Equal(DateTimeKind.Utc, conversation.CreatedUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, conversation.LastMessageUtc.Kind);

        var message = new Message();
        Assert.Equal(DateTimeKind.Utc, message.SentUtc.Kind);
    }
}

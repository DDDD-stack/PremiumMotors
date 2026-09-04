using WEBTechnologies_Final.Services;
using Xunit;

namespace PremiumMotors.Tests;

public class AppTimeTests
{
    public AppTimeTests() => AppTime.Configure("Europe/Tirane");

    [Fact]
    public void Unspecified_input_is_read_as_display_time_and_converted_to_utc()
    {
        // 1 July is CEST (UTC+2) in Tirane.
        var typed = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var utc = AppTime.FromDisplayToUtc(typed);

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void Utc_input_is_passed_through_untouched()
    {
        var utc = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(utc, AppTime.FromDisplayToUtc(utc));
    }

    [Fact]
    public void Round_trip_through_display_returns_the_original_instant()
    {
        var utc = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(utc, AppTime.FromDisplayToUtc(AppTime.ToDisplay(utc)));
    }

    [Fact]
    public void Winter_uses_the_standard_offset()
    {
        // 1 January is CET (UTC+1).
        var typed = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        Assert.Equal(new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc), AppTime.FromDisplayToUtc(typed));
    }

    [Fact]
    public void AsUtc_tags_an_unspecified_value_without_shifting_it()
    {
        // API clients send ISO 8601; an offset-less value is taken as UTC, not converted.
        var naive = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var result = AppTime.AsUtc(naive);

        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(12, result.Hour);
    }

    [Fact]
    public void Null_passes_through()
    {
        Assert.Null(AppTime.FromDisplayToUtc((DateTime?)null));
        Assert.Null(AppTime.ToDisplay((DateTime?)null));
    }
}

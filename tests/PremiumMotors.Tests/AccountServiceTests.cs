using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services;
using WEBTechnologies_Final.Services.Auth;
using Xunit;

namespace PremiumMotors.Tests;

public class AccountServiceTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"accounts-{Guid.NewGuid():N}")
            .Options);

    private static AccountService NewService(AppDbContext db) =>
        new(db, NullLogger<AccountService>.Instance);

    [Fact]
    public async Task Register_then_login_succeeds()
    {
        using var db = NewDb();
        var svc = NewService(db);

        var registered = await svc.RegisterAsync("denis", "denis@example.com", "+355691234567", "hunter2pass");
        Assert.True(registered.Succeeded);

        var login = await svc.ValidateAsync("denis", "hunter2pass");
        Assert.True(login.Succeeded);
    }

    [Fact]
    public async Task Password_is_never_stored_in_plaintext()
    {
        using var db = NewDb();
        await NewService(db).RegisterAsync("denis", "denis@example.com", "", "hunter2pass");

        var stored = (await db.Users.SingleAsync()).PasswordHash;
        Assert.DoesNotContain("hunter2pass", stored);
        Assert.StartsWith("pbkdf2-sha256$", stored);
    }

    [Fact]
    public async Task Login_works_with_the_email_address_too()
    {
        using var db = NewDb();
        var svc = NewService(db);
        await svc.RegisterAsync("denis", "denis@example.com", "", "hunter2pass");

        Assert.True((await svc.ValidateAsync("denis@example.com", "hunter2pass")).Succeeded);
    }

    [Fact]
    public async Task Duplicate_username_is_rejected_case_insensitively()
    {
        using var db = NewDb();
        var svc = NewService(db);
        await svc.RegisterAsync("denis", "a@example.com", "", "hunter2pass");

        var second = await svc.RegisterAsync("DENIS", "b@example.com", "", "hunter2pass");
        Assert.False(second.Succeeded);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected()
    {
        using var db = NewDb();
        var svc = NewService(db);
        await svc.RegisterAsync("denis", "a@example.com", "", "hunter2pass");

        Assert.False((await svc.RegisterAsync("other", "A@example.com", "", "hunter2pass")).Succeeded);
    }

    [Fact]
    public async Task A_legacy_plaintext_account_can_still_log_in_and_is_upgraded()
    {
        // Accounts created by the old MVC path stored the password verbatim.
        using var db = NewDb();
        db.Users.Add(new User { Username = "old", Email = "old@example.com", PasswordHash = "hunter2pass" });
        await db.SaveChangesAsync();

        var result = await NewService(db).ValidateAsync("old", "hunter2pass");

        Assert.True(result.Succeeded);
        Assert.StartsWith("pbkdf2-sha256$", (await db.Users.SingleAsync()).PasswordHash);
    }

    [Fact]
    public async Task A_disabled_account_cannot_log_in()
    {
        using var db = NewDb();
        var svc = NewService(db);
        var reg = await svc.RegisterAsync("denis", "denis@example.com", "", "hunter2pass");
        reg.User!.IsActive = false;
        await db.SaveChangesAsync();

        Assert.False((await svc.ValidateAsync("denis", "hunter2pass")).Succeeded);
    }

    [Fact]
    public async Task Login_failures_do_not_reveal_whether_the_account_exists()
    {
        using var db = NewDb();
        var svc = NewService(db);
        await svc.RegisterAsync("denis", "denis@example.com", "", "hunter2pass");

        var wrongPassword = await svc.ValidateAsync("denis", "nope");
        var noSuchUser = await svc.ValidateAsync("ghost", "nope");

        Assert.Equal(wrongPassword.Error, noSuchUser.Error);
    }

    [Fact]
    public async Task Changing_a_password_requires_the_current_one()
    {
        using var db = NewDb();
        var svc = NewService(db);
        var reg = await svc.RegisterAsync("denis", "denis@example.com", "", "hunter2pass");

        Assert.False((await svc.ChangePasswordAsync(reg.User!.Id, "wrong", "newpassword")).Succeeded);
        Assert.True((await svc.ChangePasswordAsync(reg.User!.Id, "hunter2pass", "newpassword")).Succeeded);
        Assert.True((await svc.ValidateAsync("denis", "newpassword")).Succeeded);
    }
}

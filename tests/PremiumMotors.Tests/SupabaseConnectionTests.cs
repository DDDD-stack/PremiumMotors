using WEBTechnologies_Final.Data;
using Xunit;

namespace PremiumMotors.Tests;

public class SupabaseConnectionTests
{
    private const string Key = "ConnectionStrings:DefaultConnection";

    [Fact]
    public void Missing_connection_string_throws_an_actionable_error()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SupabaseConnection.Build("", Key));
        Assert.Contains("user-secrets", ex.Message);
    }

    [Fact]
    public void Postgres_uri_is_converted_to_key_value_form()
    {
        // The Supabase dashboard shows the URI form by default; Npgsql only accepts key=value.
        var result = SupabaseConnection.Build(
            "postgresql://postgres.abc:secret@aws-1-eu-west-1.pooler.supabase.com:5432/postgres", Key);

        Assert.Contains("Host=aws-1-eu-west-1.pooler.supabase.com", result);
        Assert.Contains("Username=postgres.abc", result);
        Assert.Contains("Database=postgres", result);
    }

    [Fact]
    public void Percent_encoded_password_is_decoded()
    {
        var result = SupabaseConnection.Build(
            "postgresql://user:p%40ss%3Aword@host.pooler.supabase.com:5432/postgres", Key);

        Assert.Contains("p@ss:word", result);
    }

    [Fact]
    public void Ssl_is_required_when_not_specified()
    {
        var result = SupabaseConnection.Build("Host=h;Port=5432;Database=postgres;Username=u;Password=p", Key);
        Assert.Contains("SSL Mode=Require", result);
    }

    [Fact]
    public void Transaction_pooler_disables_prepared_statements()
    {
        // PgBouncer in transaction mode gives each transaction a different backend, so
        // server-side prepared statements fail intermittently.
        var result = SupabaseConnection.Build(
            "Host=aws-1-eu-west-1.pooler.supabase.com;Port=6543;Database=postgres;Username=u;Password=p", Key);

        Assert.Contains("Max Auto Prepare=0", result);
        Assert.True(SupabaseConnection.IsTransactionPooler(result));
    }

    [Fact]
    public void Session_pooler_keeps_prepared_statements()
    {
        var result = SupabaseConnection.Build(
            "Host=aws-1-eu-west-1.pooler.supabase.com;Port=5432;Database=postgres;Username=u;Password=p", Key);

        Assert.False(SupabaseConnection.IsTransactionPooler(result));
    }

    [Fact]
    public void Direct_endpoint_is_detected()
    {
        // That host is IPv6-only on the free plan, so it is worth warning about at startup.
        var result = SupabaseConnection.Build(
            "Host=db.abcdef.supabase.co;Port=5432;Database=postgres;Username=u;Password=p", Key);

        Assert.True(SupabaseConnection.IsDirectConnection(result));
    }
}

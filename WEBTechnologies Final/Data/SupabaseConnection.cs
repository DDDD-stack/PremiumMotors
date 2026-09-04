using Npgsql;

namespace WEBTechnologies_Final.Data
{
    /// <summary>
    /// Normalizes a Supabase Postgres connection string so the app behaves correctly against
    /// whichever endpoint the project is pointed at, and accepts either format the Supabase
    /// dashboard offers.
    ///
    /// Supabase exposes three endpoints and they are not interchangeable:
    ///   * db.[ref].supabase.co:5432        direct connection - IPv6-ONLY on the free plan,
    ///     so it is unreachable from most home and campus networks.
    ///   * [region].pooler.supabase.com:5432 session pooler - has IPv4, safe for EF migrations.
    ///   * [region].pooler.supabase.com:6543 transaction pooler (PgBouncer) - best for runtime,
    ///     but it multiplexes server connections, so server-side prepared statements must be
    ///     switched off or queries fail intermittently.
    /// </summary>
    public static class SupabaseConnection
    {
        /// <summary>PgBouncer transaction-mode port on the Supabase pooler.</summary>
        public const int TransactionPoolerPort = 6543;

        public static string Build(string? rawConnectionString, string configKey)
        {
            if (string.IsNullOrWhiteSpace(rawConnectionString))
            {
                throw new InvalidOperationException(
                    $"No Postgres connection string configured for '{configKey}'.\n" +
                    "Set it from your Supabase project (Connect -> ADO.NET or URI), e.g.\n" +
                    "  dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" \"Host=...;Port=5432;Database=postgres;Username=...;Password=...\"\n" +
                    "or via the ConnectionStrings__DefaultConnection environment variable. " +
                    "See docs/SUPABASE_SETUP.md.");
            }

            var raw = rawConnectionString.Trim();

            // The Supabase dashboard shows the URI form by default, so accept it as well as the
            // ADO.NET key=value form rather than failing with Npgsql's opaque
            // "Format of the initialization string does not conform to specification".
            if (LooksLikeUri(raw)) raw = ConvertUriToKeyValue(raw, configKey);

            var builder = new NpgsqlConnectionStringBuilder(raw);

            // Supabase always requires TLS. Only fill this in if the caller did not choose.
            if (!raw.Contains("SSL Mode", StringComparison.OrdinalIgnoreCase) &&
                !raw.Contains("SslMode", StringComparison.OrdinalIgnoreCase))
            {
                builder.SslMode = SslMode.Require;
            }

            // Npgsql 8 changed SslMode.Require to encrypt without verifying the certificate
            // (matching libpq), and retired TrustServerCertificate as a no-op. Use VerifyFull
            // in the connection string if you want the certificate chain checked.

            if (builder.Port == TransactionPoolerPort)
            {
                // PgBouncer in transaction mode hands each transaction a different server
                // connection, so neither prepared statements nor session state can survive.
                builder.MaxAutoPrepare = 0;
                builder.NoResetOnClose = true;
            }

            if (string.IsNullOrWhiteSpace(builder.ApplicationName))
            {
                builder.ApplicationName = "PremiumMotors";
            }

            return builder.ConnectionString;
        }

        private static bool LooksLikeUri(string value) =>
            value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Converts postgresql://user:password@host:port/database?params into the key=value form
        /// Npgsql expects. Credentials are percent-decoded, which matters because Supabase encodes
        /// any special character in a generated password.
        /// </summary>
        private static string ConvertUriToKeyValue(string uriString, string configKey)
        {
            if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
            {
                throw new InvalidOperationException(
                    $"The value of '{configKey}' looks like a postgres:// URI but could not be parsed. " +
                    "If your password contains an '@' or '/', copy the ADO.NET form from the Supabase " +
                    "Connect dialog instead. See docs/SUPABASE_SETUP.md.");
            }

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port > 0 ? uri.Port : 5432,
                Database = uri.AbsolutePath.Trim('/') is { Length: > 0 } db ? db : "postgres"
            };

            var userInfo = uri.UserInfo ?? string.Empty;
            if (userInfo.Length > 0)
            {
                var separator = userInfo.IndexOf(':');
                if (separator >= 0)
                {
                    builder.Username = Uri.UnescapeDataString(userInfo[..separator]);
                    builder.Password = Uri.UnescapeDataString(userInfo[(separator + 1)..]);
                }
                else
                {
                    builder.Username = Uri.UnescapeDataString(userInfo);
                }
            }

            // Carry over sslmode= from the query string if the URI specified one.
            var query = uri.Query.TrimStart('?');
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length != 2) continue;

                if (string.Equals(kv[0], "sslmode", StringComparison.OrdinalIgnoreCase) &&
                    Enum.TryParse<SslMode>(kv[1], ignoreCase: true, out var sslMode))
                {
                    builder.SslMode = sslMode;
                }
            }

            return builder.ConnectionString;
        }

        /// <summary>True when this endpoint is the PgBouncer transaction pooler.</summary>
        public static bool IsTransactionPooler(string connectionString) =>
            new NpgsqlConnectionStringBuilder(connectionString).Port == TransactionPoolerPort;

        /// <summary>
        /// True for the direct db.[ref].supabase.co endpoint, which has no A record on the free
        /// plan and so is unreachable from IPv4-only networks.
        /// </summary>
        public static bool IsDirectConnection(string connectionString)
        {
            var host = new NpgsqlConnectionStringBuilder(connectionString).Host ?? string.Empty;
            return host.StartsWith("db.", StringComparison.OrdinalIgnoreCase)
                && host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase);
        }
    }
}

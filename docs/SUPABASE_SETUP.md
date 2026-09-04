# Supabase setup

The app no longer ships a database password in `appsettings.json`. Both the Postgres
connection string and the JWT signing key are read from **user-secrets** (local development)
or **environment variables** (hosting).

## 1. Get the connection string from Supabase

In your Supabase project: **Project Settings → Database → Connection string → ADO.NET**.

Supabase exposes three endpoints and they are *not* interchangeable:

| Endpoint | Host / port | Use it for |
|---|---|---|
| Direct | `db.<ref>.supabase.co:5432` | IPv6-only on the free plan; usually not what you want from a laptop |
| Session pooler | `<region>.pooler.supabase.com:5432` | **EF Core migrations** and general use — recommended default |
| Transaction pooler | `<region>.pooler.supabase.com:6543` | High-concurrency runtime (PgBouncer) |

`Data/SupabaseConnection.cs` normalizes whatever you supply:

* forces `SSL Mode=Require` if you did not specify one (Supabase always requires TLS);
* if the port is `6543`, it sets `Max Auto Prepare=0` and `No Reset On Close=true`, because
  PgBouncer in transaction mode gives each transaction a different backend and server-side
  prepared statements would otherwise fail intermittently.

**Start with the session pooler on port 5432.** The app runs `Database.MigrateAsync()` at
startup, and DDL through the transaction pooler is unreliable.

## 2. Store it locally

From the `WEBTechnologies Final` folder:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=aws-0-<region>.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<project-ref>;Password=<your-db-password>"
```

The JWT signing key has already been generated and stored for you. To see or replace it:

```bash
dotnet user-secrets list
dotnet user-secrets set "Jwt:Key" "<64+ random characters>"
```

`Jwt:Key` must be at least 32 bytes; the app refuses to start otherwise, with a message
telling you exactly what to set.

## 3. Run it

```bash
dotnet run
```

On first start the app applies all migrations to your Supabase database and seeds an
administrator account (`admin` / `admin123` in Development — change it with
`dotnet user-secrets set "AdminSeed:Password" "..."`).

## Hosting

Use environment variables instead of user-secrets — double underscore maps to the config
colon:

```
ConnectionStrings__DefaultConnection=Host=...;Port=5432;Database=postgres;Username=...;Password=...
Jwt__Key=<64+ random characters>
Jwt__Issuer=PremiumMotors
Jwt__Audience=PremiumMotors.Clients
AdminSeed__Password=<a real password>
Cors__AllowedOrigins__0=https://your-web-app
Cors__AllowedOrigins__1=https://your-other-client
```

If `Cors:AllowedOrigins` is empty the API allows any origin **without** credentials, so a
misconfiguration cannot turn into a session-riding hole. Native mobile apps are not subject
to CORS at all.

## Troubleshooting

| Symptom | Cause |
|---|---|
| `No Postgres connection string configured` | The secret is not set — see step 2. |
| `Jwt:Key is missing or shorter than 32 bytes` | Set `Jwt:Key`. |
| `prepared statement "_p1" already exists` | You are on port 6543 with a connection string that overrides `Max Auto Prepare`. Use port 5432 or drop the override. |
| Connection timeouts from home broadband | You are on the direct `db.<ref>.supabase.co` host, which is IPv6-only on the free plan. Use a pooler host. |
| `password authentication failed` | On pooler endpoints the username is `postgres.<project-ref>`, not `postgres`. |

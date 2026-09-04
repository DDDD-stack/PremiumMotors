# Setting up on another machine

Everything in this repository is source. Everything that is a *secret* is not, by
design, so a fresh clone will not run until you put three values back. This page
is the complete list.

---

## 1. Prerequisites

| Tool | Version | Check |
|---|---|---|
| .NET SDK | 9.0 | `dotnet --version` |
| Git | any recent | `git --version` |

Postgres is **not** installed locally — the app talks to Supabase.

---

## 2. Clone

```bash
git clone https://github.com/DDDD-stack/PremiumMotors.git
cd PremiumMotors
```

---

## 3. Put the secrets back

None of these are in the repository, and none of them should ever be. They live
in .NET **user-secrets**, which is a file in your Windows/macOS user profile
outside the project folder.

```bash
cd "WEBTechnologies Final"

dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<supabase connection string>"
dotnet user-secrets set "Jwt:Key"                            "<random 32+ byte string>"
dotnet user-secrets set "AdminSeed:Password"                  "<a password you choose>"
```

Where to get each one:

- **ConnectionStrings:DefaultConnection** — Supabase dashboard → Project Settings
  → Database → Connection string → **Session pooler** (port 5432) while you are
  developing and running migrations. See `docs/SUPABASE_SETUP.md`. In production
  use the transaction pooler on port 6543; the app warns you if you don't.
- **Jwt:Key** — any long random string; it only has to match itself. Generate one
  with `openssl rand -base64 48`. A different key on each machine is fine — it
  just invalidates mobile tokens issued by the other one.
- **AdminSeed:Password** — the password for the seeded `admin` account. In
  Development the seed falls back to `admin123` if you skip it.

To confirm they landed (this prints keys and values, so don't paste the output
anywhere):

```bash
dotnet user-secrets list
```

### `.env` (optional)

`.env` is gitignored and holds the Supabase **publishable** URL and key, used by
tooling rather than by the app itself. Copy the template and fill it in if you
need it:

```bash
cp .env.example .env
```

---

## 4. Run

```bash
dotnet run --project "WEBTechnologies Final/WEBTechnologies Final.csproj" --launch-profile https
```

- App: <https://localhost:7007>
- Health: `/health/live` and `/health/ready`
- Swagger (Development only): `/swagger`

Migrations apply automatically at startup (`Database:AutoMigrate`, default on),
so the schema comes up to date on its own. To run them by hand:

```bash
dotnet ef database update --project "WEBTechnologies Final/WEBTechnologies Final.csproj"
```

Tests:

```bash
dotnet test tests/PremiumMotors.Tests/PremiumMotors.Tests.csproj
```

---

## 5. Two things that will look broken and are not

**Uploaded photos 404.** `wwwroot/uploads/` is gitignored — those are runtime
user files, not source, and committing multi-megabyte uploads would put them in
the history forever. Listings created on the *other* machine still have their
photo paths in the shared database, but the files themselves are on that
machine's disk. New uploads work fine.

This disappears the moment photo storage moves to Supabase (`Storage:Provider`
= `Supabase` plus `Storage:SupabaseServiceKey`), which is item 1.1 of the
pre-release checklist and the real fix. Until then, expect broken thumbnails for
anything uploaded elsewhere.

**You are signed out.** Sessions now live in the database rather than in process
memory, so they survive a restart — but the session cookie is per-browser. Just
log in again.

---

## 6. Working on both machines without stepping on yourself

The database is **shared**: both machines point at the same Supabase project. So
a schema change made on one is live for the other immediately, whether or not the
code has been pushed.

That has one practical consequence worth remembering:

> If you add a migration on machine A and run the app, the database is migrated.
> Machine B's older code then talks to a newer schema. It will usually work
> — the migrations here are additive — but pull before you run.

The habit that avoids all of it:

```bash
git pull --rebase     # before you start
git push              # before you switch machines
```

If you ever want the two machines fully independent, give each its own Supabase
project and set a different `ConnectionStrings:DefaultConnection` on each. Nothing
in the code assumes a shared database.

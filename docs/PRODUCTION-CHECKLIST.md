# Production checklist

Work top to bottom. Everything here is configuration or content — the code side is done.

## 1. Environment

```bash
ASPNETCORE_ENVIRONMENT=Production
```

Set this **explicitly**. Deploying as `Development` seeds the admin account with `admin123`,
publishes Swagger, drops `Secure` from the session cookie and exposes detailed errors — four
failures from one mistake.

## 2. Secrets (environment variables; `__` maps to `:`)

| Variable | Required | Notes |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | yes | Supabase **session pooler**, port 5432 |
| `Jwt__Key` | yes | 32+ bytes. Changing it signs everyone out |
| `AdminSeed__Password` | yes | Otherwise a random one is generated and only logged |
| `App__PublicBaseUrl` | yes | `https://yourdomain` — image URLs depend on it |
| `Storage__Provider` | yes | `Supabase` |
| `Storage__SupabaseServiceKey` | yes | service_role key. Server-side only |
| `Email__ApiKey`, `Email__From` | yes | Without these, password reset silently does nothing |
| `Cors__AllowedOrigins__0` | yes | Empty list falls back to allow-any-origin |
| `Database__AutoMigrate` | recommended | `false`, then migrate as a deploy step |

## 3. Database

```bash
dotnet ef database update
```

Run this as an explicit deploy step with `Database__AutoMigrate=false`. Two instances starting
together would otherwise race, and a failed migration would take the site down instead of
failing the deploy.

## 4. Storage

Create a **public** `car-photos` bucket in Supabase Storage. Without `Storage__Provider=Supabase`
the app writes to local disk, which most hosts wipe on every deploy — losing every uploaded photo.

## 5. Content and legal

- Real Terms and Privacy content
- Cookie/consent banner
- Explain the sealed-offer model on the site: offers don't have to beat each other, the seller
  sees all of them, the highest wins at close and the seller then gets the buyer's contact
  details. Users will assume eBay rules otherwise.

## 6. Verify after deploy

```bash
curl https://yourdomain/health/ready          # 200
curl https://yourdomain/api/v1/cars           # 200, absolute https:// image URLs
curl https://yourdomain/swagger               # 404
```

- Log in as `admin` / `admin123` — this **must fail**.
- Request one real password reset and confirm the email arrives.
- Upload a photo and confirm the URL points at Supabase Storage.

## 7. Known gaps at this scale

- Sessions are in process memory: web logins are lost on restart and are not shared between
  instances. Move to Redis before scaling out. The mobile app is unaffected — JWT is stateless.
- Rate limiting is per-instance and per-IP. With N instances the effective limit is N×, and users
  behind one NAT share a budget.
- No error tracking yet. Add Sentry or Application Insights before you rely on this.

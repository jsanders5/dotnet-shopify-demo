# Shopify Inventory Sync & Storefront Alert — .NET backend (Day 1)

A small ASP.NET Core Web API that models Shopify inventory data and
receives Shopify inventory-update webhooks. Built as a scoped, honest demo
closing real .NET/Shopify skill gaps ahead of applying to a specific job
posting — see `CLAUDE.md` for why this exists and what it does and doesn't
claim, and `PLAN.md` for the full build plan.

This is Day 1 of a 2-day plan. Day 2 (Shopify Partner store, Liquid theme
code, live webhook wiring) has not been built yet — see "Not here yet"
below.

## What's here (Day 1 scope only)

- ASP.NET Core 8 Web API, controller-based
- EF Core Code-First models (`Product`, `InventoryLog`) and a first
  migration, against a SQL Server-compatible engine (see caveat below)
- `GET /api/products`, `GET /api/products/{id}`, `POST /api/products`
- `GET /api/products/low-stock` — backed by a real `GetLowStockProducts`
  stored procedure, called via EF Core's `FromSqlRaw` (verified manually,
  not by automated tests — see caveat below)
- `POST /webhooks/inventory-update` — verifies the `X-Shopify-Hmac-Sha256`
  signature before persisting, using the real Shopify
  `inventory_levels/update` payload shape (`inventory_item_id`, `available`)
- Configured for in-process IIS hosting (`AspNetCoreHostingModel` set to
  `InProcess`, `web.config` generated correctly on `dotnet publish`) — this
  demonstrates the app *is* IIS-deployable, but it has **not** actually
  been deployed to or run under a real IIS instance. See caveat below.
- xUnit tests for the CRUD endpoints, the webhook receiver, and the HMAC
  verifier, run against EF Core's InMemory provider

## Not here yet (Day 2 scope, see `PLAN.md`)

- No Shopify Partner account, no development store, no Liquid theme code
- No live Shopify webhook wiring — the webhook receiver is built and
  tested against hand-crafted HTTP requests carrying a valid HMAC
  signature, not against an actual Shopify store sending real events
- No storefront "low stock" badge, no App Proxy, no end-to-end demo yet

## Honest caveats

- **"SQL Server" locally is Azure SQL Edge, not the genuine SQL Server
  engine.** Microsoft's Linux SQL Server container image has no arm64
  build, so this Apple Silicon dev machine runs
  `mcr.microsoft.com/azure-sql-edge` instead, via Colima. It's
  wire-compatible (same T-SQL, same EF Core `SqlServer` provider, stored
  procedures work the same way), but it's a different product from real
  SQL Server, and that distinction matters if asked directly.
- **No live IIS deployment.** The app runs on Kestrel locally and is
  configured for IIS's in-process hosting model (`web.config` is
  generated correctly by `dotnet publish`), but it has never actually been
  deployed to or run under a real IIS instance — this dev environment is
  macOS, and IIS is Windows-only.
- **The low-stock endpoint is verified manually, not by automated tests.**
  EF Core's InMemory provider (used by the xUnit suite) can't execute
  `FromSqlRaw`, so `GetLowStockProducts` is confirmed working via a
  documented manual `curl` sequence against the real container instead of
  an automated test. See "Verifying the low-stock stored procedure" below.
- **The Azure SQL Edge container has no `sqlcmd` tooling at all** (checked
  via `dpkg -l` inside the running container — no `/opt/mssql-tools` or
  `/opt/mssql-tools18`). `docker-compose.yml`'s healthcheck therefore uses
  a plain bash `/dev/tcp` TCP-connect probe instead of a `sqlcmd -Q`
  query — it confirms the port is open, not that the engine accepts a
  login and runs a query.

## Running locally

Requires the .NET 8 SDK and a container runtime (Colima + `docker compose`
on macOS; Docker Desktop elsewhere should also work). This project's SDK
was installed via Microsoft's install script rather than a brew cask —
see `docs/superpowers/plans/2026-07-22-day1-dotnet-backend.md` (Task 1)
for the exact commands if you need to replicate that.

```bash
cp .env.example .env   # edit in a real password
docker compose up -d
docker compose ps      # wait for the db service to report healthy
export $(grep -v '^#' .env | xargs)
dotnet ef database update --project src/InventorySync.Api
dotnet run --project src/InventorySync.Api
```

The API listens on `http://localhost:5072` (HTTP profile; see
`src/InventorySync.Api/Properties/launchSettings.json`). Swagger UI is at
`http://localhost:5072/swagger` in Development.

**Troubleshooting `dotnet ef`:** if `dotnet-ef` fails to resolve
`libhostfxr.dylib`, it needs `DOTNET_ROOT` set, not just `PATH`:
```bash
export DOTNET_ROOT="$HOME/.dotnet"
```

## Running tests

```bash
dotnet test tests/InventorySync.Api.Tests
```
All of these run against EF Core's InMemory provider — no container
needed. The one thing they don't cover is the `GetLowStockProducts`
stored procedure itself (InMemory can't execute `FromSqlRaw`); see below.

## Verifying the low-stock stored procedure (manual — not covered by xUnit)

With the container running and migrations applied:
```bash
dotnet run --project src/InventorySync.Api &
sleep 3
curl -s -X POST http://localhost:5072/api/products -H "Content-Type: application/json" \
  -d '{"shopifyInventoryItemId":1,"title":"Low","sku":"LOW","quantity":2,"lowStockThreshold":5}'
curl -s -X POST http://localhost:5072/api/products -H "Content-Type: application/json" \
  -d '{"shopifyInventoryItemId":2,"title":"High","sku":"HIGH","quantity":50,"lowStockThreshold":5}'
curl -s http://localhost:5072/api/products/low-stock
kill %1
```
Expected (and confirmed): the last call returns a JSON array containing
only the `"sku":"LOW"` product — the quantity-2 product against a
threshold of 5, not the quantity-50 one.

## Project layout

```
src/InventorySync.Api/         ASP.NET Core Web API
  Controllers/                 ProductsController, WebhooksController
  Data/                        AppDbContext
  Migrations/                  EF Core migrations (incl. the stored procedure)
  Models/                      Product, InventoryLog, InventoryUpdatePayload
  Services/                    ShopifyHmacVerifier
tests/InventorySync.Api.Tests/ xUnit tests (InMemory-backed)
docker-compose.yml             Azure SQL Edge container (local "SQL Server")
docs/superpowers/plans/        Detailed, corrected-after-the-fact build log
```

For the full, corrected-after-the-fact record of exactly what commands
were run and what deviated from the original plan (SDK install method,
port number, healthcheck approach, `dotnet-ef` environment quirk, etc.),
see `docs/superpowers/plans/2026-07-22-day1-dotnet-backend.md` — it was
kept up to date as each task was completed and is the most accurate
account of what actually happened.

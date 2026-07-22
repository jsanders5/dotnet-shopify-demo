# Build Plan: Shopify Inventory Sync & Storefront Alert

2-day plan to build one small, real, working integration that honestly
closes the .NET/Shopify gaps in `job-posting.md`, rather than four
disconnected toy demos. See `CLAUDE.md` for the non-negotiable honesty rule
before touching this.

## Why this shape

The role itself is ".NET backend + Shopify storefront," so a project that
connects the two mirrors the actual job instead of just checking keyword
boxes.

## Gaps this addresses

- ASP.NET Core / Entity Framework / IIS — no prior hands-on experience
- Shopify Liquid / theme development — zero prior experience (the biggest
  gap)
- Microsoft SQL Server — prior DB experience is DynamoDB/Supabase
  (Postgres), not SQL Server specifically

## Architecture

- **ASP.NET Core Web API** — Products/InventoryLog endpoints, DI, middleware
- **EF Core + SQL Server** (Docker container or LocalDB) — Code-First
  models, a migration, and at least one raw stored procedure (e.g.
  `GetLowStockProducts`) called via `FromSqlRaw` — hits "stored procedures,
  data modeling, CRUD" directly
- **Webhook receiver** — Shopify calls it on inventory changes; verifies the
  HMAC signature (a real, correct integration pattern, not a toy)
- **Shopify theme customization** — a Liquid section/snippet on a free
  Shopify Partner dev store's product page (e.g. a "low stock" badge),
  calling the .NET API live
- Built with Claude Code/Opus throughout — provable via commit history,
  which itself satisfies the posting's "AI-assisted development tools" line

## Day 1 — .NET backend

- [x] Scaffold ASP.NET Core Web API project (controllers, DI)
- [x] Stand up a SQL Server-compatible engine (Azure SQL Edge via Colima —
      see "What was decided" below; not real SQL Server, not LocalDB)
- [x] Define EF Core models (`Product`, `InventoryLog`), run first
      migration
- [x] Build CRUD endpoints (GET/POST products, low-stock report)
- [x] Add one stored procedure (`GetLowStockProducts`) called via
      `FromSqlRaw`, to explicitly demonstrate stored-procedure experience
- [x] Add a webhook receiver endpoint (`POST /webhooks/inventory-update`)
      that verifies the Shopify HMAC signature and persists updates
- [x] Basic tests (xUnit, EF Core InMemory provider; the stored procedure
      itself is verified manually instead — InMemory can't run
      `FromSqlRaw`, see `README.md`)

## Day 2 — Shopify half + wiring

- [ ] Free Shopify Partner account + development store (Dawn base theme)
- [ ] Add a custom Liquid section/snippet (e.g. "Low Stock Alert" badge on
      the product page) using Liquid tags/objects
- [ ] Wire the storefront section to call the .NET API (via a small JS
      fetch, ideally through a Shopify App Proxy so it's same-origin)
- [ ] Configure a real Shopify webhook pointing at the .NET webhook endpoint
- [ ] Test end-to-end: change inventory in Shopify admin → webhook fires →
      .NET verifies + updates SQL Server → storefront badge reflects the
      change
- [ ] Polish: README, architecture diagram, screenshots/GIF, push to GitHub
- [ ] Draft resume bullets from what was actually built (no more, no less)

## Known constraint: IIS

Local dev environment is macOS; IIS is Windows-only, so there's no local IIS
to deploy to. Two honest options:

1. Deploy to Azure App Service on a Windows plan — genuinely IIS under the
   hood, if Azure access is available.
2. Skip a live IIS deployment and say so plainly. Ship on Kestrel with
   `web.config` and the in-process hosting model configured correctly (so
   the app is demonstrably IIS-deployable) without claiming it was actually
   run under IIS.

Default to option 2 unless Azure access is confirmed available.

**Resolved:** No Azure access was confirmed, so option 2 was taken. The
app runs on Kestrel, is configured with `AspNetCoreHostingModel=InProcess`,
and `dotnet publish` was confirmed to generate a correct `web.config` —
but it has never been deployed to or run under a real IIS instance. See
`README.md` for the full caveat.

## What was decided (Day 1 complete)

The items below were open questions before implementation started; here's
what was actually decided/done, so this file reflects reality rather than
lingering as unresolved:

- **SQL engine:** Azure SQL Edge (`mcr.microsoft.com/azure-sql-edge`) run
  via Colima, not a real SQL Server container and not LocalDB — the real
  Linux SQL Server image has no arm64 build, and this dev machine is
  Apple Silicon macOS. Wire-compatible T-SQL/EF Core surface, but a
  different product; see `README.md`'s caveats section.
- **Shopify Partner account / development store:** not set up yet — this
  is Day 2 scope, not started.
- **Repo visibility:** left as the user's call to make when ready to link
  it from a resume; not decided as part of Day 1 backend work.

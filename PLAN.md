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

## Phase 1 — .NET backend

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

## Phase 2 — Shopify half + wiring

- [x] Free Shopify Partner account + development store (`inventory-sync-demo`
      — default theme turned out to be a Horizon-based theme, not Dawn;
      see "What was decided" below)
- [x] Add a custom Liquid section/snippet ("Low Stock Alert" badge on the
      product page), rendered as a `main-product` block right after
      price, styled to match the theme's own native low-stock indicator
- [x] Wire the storefront section to call the .NET API via a small JS
      fetch through a Shopify App Proxy (same-origin)
- [x] Configure a real Shopify webhook pointing at the .NET webhook endpoint
- [x] Test end-to-end: change inventory in Shopify admin → webhook fires →
      .NET verifies + updates SQL Server → storefront badge reflects the
      change — confirmed live, in both directions, including switching
      between variants without a page reload
- [x] Polish: README updated for Phase 2; screenshots taken during live
      verification (not saved as repo assets — see note below)
- [ ] Draft resume bullets from what was actually built — in progress,
      being handled directly with the user rather than as a repo artifact
- [ ] Push to GitHub — not yet done, repo visibility still the user's call
      (see Phase 1's note)
- [ ] Architecture diagram — not built; the README's prose description
      and the two plan docs stand in for one

Note: no GIF/screenshot files were added to this repo. Verification
screenshots were taken during the session and sent directly to the user,
not committed — if a demo GIF is wanted for the resume/portfolio, that's
still open.

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

## What was decided (Phase 1 complete)

The items below were open questions before implementation started; here's
what was actually decided/done, so this file reflects reality rather than
lingering as unresolved:

- **SQL engine:** Azure SQL Edge (`mcr.microsoft.com/azure-sql-edge`) run
  via Colima, not a real SQL Server container and not LocalDB — the real
  Linux SQL Server image has no arm64 build, and this dev machine is
  Apple Silicon macOS. Wire-compatible T-SQL/EF Core surface, but a
  different product; see `README.md`'s caveats section.
- **Shopify Partner account / development store:** set up — a
  Partner account and a development store, `inventory-sync-demo`, with
  Shopify's "generate test data" option (real sample products, e.g. "The
  Complete Snowboard" with 5 variants) and no feature previews enabled.
- **Repo visibility:** left as the user's call to make when ready to link
  it from a resume; not decided as part of this work.

## What was decided (Phase 2 complete)

- **App registration mechanism:** legacy custom apps (created directly
  from a store's own Admin) are disabled for new creation as of January
  2026. Used Shopify's current path instead: Dev Dashboard → "Start with
  Shopify CLI" → `npx @shopify/create-app@latest --template none`,
  stripped down from its scaffolded demo "FAQ" app (embedded UI,
  metaobjects, an MCP extension — all irrelevant) to just
  `shopify-app/shopify.app.toml`: declarative App Proxy + webhook
  subscription config, no app code of its own. Deployed via
  `shopify app deploy --allow-updates`.
- **Storefront reachability:** a Cloudflare Quick Tunnel (no account
  signup required, unlike ngrok's free tier) rather than a real
  deployment — see README's caveats.
- **Liquid `inventory_item_id` gap:** Shopify's Liquid environment does
  not expose `variant.inventory_item_id` at all (confirmed live). Added
  a `ShopifyVariantId` field and a `by-variant` lookup endpoint instead;
  the webhook receiver is unaffected and still keys on
  `ShopifyInventoryItemId`, matching Shopify's real webhook payload.
- **Badge placement and styling:** moved from a standalone section
  (bottom of page) to a `main-product` block right after price, matching
  the theme's own native low-stock indicator styling. Two real bugs
  found and fixed during live verification: a missing script tag from
  converting a section into a snippet, and this theme's
  `.product__inventory` CSS unconditionally setting `display: flex`,
  which silently defeated the `hidden` attribute regardless of selector
  specificity.
- **Multi-location inventory:** found live (the dev store's own
  "Multi-location Snowboard" sample product genuinely has inventory
  split across two locations) that this demo does not aggregate
  correctly across locations. Disclosed as a known limitation rather
  than fixed — see README.
- **Full catalog registration:** all 17 sample products (28 rows,
  including two synthetic test rows from Phase 1) registered via a one-off
  manual script — not a built "sync" feature.

## Phase 3 — bonus: RAG product Q&A (complete)

Not in the original 2-day scope — added afterward to demonstrate AI-tool
proficiency using the same App Proxy infrastructure already built, rather
than as a disconnected add-on. Design: `docs/superpowers/specs/2026-07-22-phase3-rag-product-qa-design.md`.
Plan: `docs/superpowers/plans/2026-07-22-phase3-rag-product-qa.md`.

- [x] Content model (`ProductGuide`/`ProductGuideChunk`) + migration
- [x] Cosine similarity retrieval (pure C#, unit tested)
- [x] Voyage AI embedding client (`voyage-3.5`) — Anthropic has no
      embeddings endpoint of its own
- [x] Claude Haiku 4.5 answer client, grounded vs. ungrounded prompts
- [x] `POST /api/products/{id}/ask` endpoint, tested against EF InMemory
      + fake clients
- [x] Anthropic + Voyage API keys set via `dotnet user-secrets`
- [x] Guide content (4 products x 5 chunks, original writing based on
      real Skullcandy support-page facts) seeded via a throwaway script,
      not committed — only the content JSON is
- [x] Storefront "Ask about this product" box, reusing the Phase 2 App
      Proxy, pushed live and verified against the real dev store
- [x] ANC trap-question demo, run live against two real products —
      transcripts recorded verbatim in README
- [x] README + this file documented

**What was decided (Phase 3):**
- **Voyage AI free-tier rate limit (3 requests/minute without a payment
  method):** the seeding script originally sent one embedding request
  per chunk (20 requests for 20 chunks) and hit the limit partway
  through, leaving partial data. Fixed by batching each product's 5
  chunks into a single Voyage request (4 requests total) plus
  idempotent cleanup and retry/backoff — found and fixed live during
  seeding, not anticipated in the design.
- **No error handling around external API calls, found live:** the
  original `/ask` implementation had no try/catch around the
  Voyage/Claude HTTP calls, so a live 429 during manual testing
  surfaced as a raw unhandled exception rather than a controlled
  response. Fixed to return a `502` with a clear message on failure —
  still no retry logic, which stays a disclosed simplification.
- **Ungrounded prompt includes the product name:** the initial
  implementation sent the *bare* question with zero product
  identification to the "without context" call, which just tested
  whether the model could guess the product rather than whether it had
  accurate documentation. Fixed to include the product name (known from
  any real product page, RAG or not) so the comparison isolates the
  actual variable — documentation vs. none.
- **ANC trap-question demo, actual vs. predicted:** the design doc
  predicted the ungrounded answer would confidently hallucinate
  ANC steps. Confirmed true once the prompt included the product name
  (see above) — without it, Claude Haiku asked a clarifying question
  instead of guessing, which was a weaker, less honest demo. The final
  recorded transcripts (see README) show the predicted failure mode:
  confident, plausible-sounding, and factually wrong steps (invented
  button/mode names) on both the ANC and non-ANC models.

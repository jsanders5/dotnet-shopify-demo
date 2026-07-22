# Phase 2 — Shopify Storefront Integration Design

**Status:** Approved, ready for implementation planning.

## Goal

Connect the Phase 1 .NET backend to a real Shopify development store: a
Liquid "Low Stock Alert" badge on the product page, backed by a live
call through a Shopify App Proxy to the .NET API, with a real Shopify
webhook driving inventory updates end-to-end. This is the half of the
project that closes the biggest actual skill gap named in `CLAUDE.md`
(Shopify Liquid/theme development — zero prior experience).

## Prerequisites (done)

- Shopify Partner account created.
- Development store created (test data generated, no feature preview
  opted in). Default theme TBD — confirm during implementation; the
  approach below is theme-agnostic (works on any Online Store 2.0
  theme, not specifically Dawn).

## Architecture

- **Tunnel:** Cloudflare Quick Tunnel (`cloudflared tunnel --url
  http://localhost:5072`) exposes the local .NET API (from Phase 1) on a
  public URL. No account/signup required. Documented plainly as a dev
  tunnel, not a production deployment — consistent with `CLAUDE.md`'s
  honesty rule.
- **Shopify custom app:** registered via the Partner Dashboard (not a
  full CLI-scaffolded Node/Remix app — the real backend is the existing
  .NET API, so the app registration exists only to provide App Proxy
  and webhook configuration). Provides:
  - **App Proxy** config: subpath `/apps/inventory`, forwarded by
    Shopify to the tunnel URL, making the storefront's call
    same-origin.
  - **Webhook subscription** for the `inventory_levels/update` topic,
    pointed at `{tunnel}/webhooks/inventory-update`, with a real
    HMAC signing secret (replaces the test placeholder from Phase 1,
    set via `dotnet user-secrets` on the .NET side).
- **New backend endpoint:** `GET
  /api/products/by-inventory-item/{inventoryItemId}` on the existing
  `ProductsController` — none of Phase 1's endpoints let the storefront
  ask "is *this specific* product low stock," which is what the badge
  needs. Returns a neutral "not low stock" response (not 404) for an
  untracked item, consistent with the pattern the Phase 1 final review
  settled on for the webhook receiver.
- **Liquid section/snippet:** a "Low Stock Alert" badge added to the
  product page template, developed via `shopify theme dev` for live
  reload against the dev store.
- **Storefront JS:** a small vanilla `fetch` (Dawn/current default
  themes don't ship jQuery, so plain `fetch` is the idiomatic choice
  here, not jQuery) that calls the App Proxy path same-origin on page
  load and shows/hides the badge based on the response.

## Data Flow

Shopify Admin inventory edit → real Shopify webhook fires →
tunnel → `WebhooksController` verifies HMAC with the real secret →
`Product`/`InventoryLog` updated in Azure SQL Edge (same Phase 1 code,
now driven by a live Shopify event) → storefront visitor loads the
product page → Liquid renders the badge container → JS fetches
`/apps/inventory/products/{inventoryItemId}` via the App Proxy
(same-origin) → Shopify forwards to the tunnel → API responds with
current stock status → JS shows/hides the "Low Stock" badge.

## Error Handling

- Tunnel/API unreachable: the badge simply doesn't render (console-
  logged error only) — this is a best-effort storefront enhancement,
  not core checkout functionality, so it must never break the page.
- Invalid/missing webhook signature: same 401 behavior as Phase 1;
  Shopify's normal retry policy applies.
- Unknown `inventory_item_id` (no matching `Product` row): the new
  endpoint returns a neutral/"not low stock" response rather than 404,
  same reasoning as the Phase 1 webhook receiver's 200-on-unknown-item
  fix.

## Testing

- New backend endpoint (`by-inventory-item/{id}`) gets an InMemory-
  backed xUnit test, consistent with the rest of the API test suite.
- No automated tests for the Liquid/JS side — nothing meaningful to
  unit-test in that layer for a project this size. Verified instead by
  a documented, reproducible manual end-to-end run (same precedent as
  Phase 1's stored-procedure verification): change a variant's inventory
  in Shopify Admin, confirm the webhook fires and updates the DB,
  reload the storefront product page, confirm the badge reflects the
  new state.

## Global Constraints (carried forward from Phase 1's plan + CLAUDE.md)

- Never overstate what's built — the tunnel is a dev tunnel, not a
  deployment; the custom app exists only for App Proxy/webhook config,
  not as a real published Shopify app.
- Keep it tight: one Liquid feature (the low-stock badge), one new
  backend endpoint, no additional storefront features beyond what's
  described here.
- Real webhook secret lives only in `dotnet user-secrets` / the
  Shopify app's own config — never committed.
- Exact Shopify CLI commands and App Proxy/webhook-subscription setup
  mechanics will be confirmed live against the actual dev store during
  implementation (the first implementation task) rather than
  hard-specified here — Phase 1 showed these environment/platform
  specifics often need on-the-ground correction, so the implementation
  plan should expect and record adjustments the same way.

## Out of Scope (this spec)

- The RAG/AI Q&A bonus feature discussed separately — a distinct
  sub-project, planned after this one.
- Any further Liquid features beyond the single low-stock badge.
- Production deployment of the .NET API (still Kestrel + tunnel, per
  Phase 1's IIS honesty caveat).

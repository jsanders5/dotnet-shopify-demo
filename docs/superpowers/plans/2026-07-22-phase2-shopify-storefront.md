# Phase 2 — Shopify Storefront Integration Implementation Plan

> **For agentic workers:** Tasks marked **[CODE]** use
> superpowers:subagent-driven-development (dispatch a fresh implementer
> subagent, review, repeat) exactly like Phase 1. Tasks marked **[USER
> ACTION]** involve Shopify's own web UI (Partner Dashboard, store
> admin) or an interactive CLI login tied to the user's account — these
> cannot be dispatched to a subagent (no subagent can complete an OAuth
> login or click through someone else's dashboard) and are done by the
> human, in this session, with the controller providing exact
> instructions and verifying the result afterward. Tasks marked
> **[INFRA]** are local tooling/process setup with no login involved —
> the controller runs these directly via Bash, same as Phase 1's
> Colima/Docker setup.

**Goal:** Wire the Phase 1 .NET API to a real Shopify development store: a
Liquid "Low Stock Alert" badge on the product page, backed by a live
App Proxy call, driven end-to-end by a real Shopify webhook.

**Architecture:** See
`docs/superpowers/specs/2026-07-22-phase2-shopify-storefront-design.md`
for the full design and reasoning. Summary: Cloudflare Quick Tunnel
exposes the local API; a Shopify custom app (Partner Dashboard) holds
the App Proxy config and the real webhook subscription; a new .NET
endpoint answers "is this product low stock"; a Liquid section + small
JS fetch renders the badge.

## Global Constraints

- Never overstate what's built (CLAUDE.md's hard rule, carried forward
  from Phase 1): the tunnel is a dev tunnel, not a deployment; the custom
  app exists only for App Proxy/webhook config, not as a published app.
- Real secrets (the app's client secret / webhook signing secret) are
  set via `dotnet user-secrets` directly by the human in their own
  terminal — never pasted into chat, never committed.
- Exact Shopify Partner Dashboard UI labels/steps below are written
  from current knowledge of Shopify's App Proxy and webhook
  configuration screens, but Shopify's UI changes over time. If a step
  doesn't match what's actually on screen, adapt to what's there and
  correct this plan doc afterward — same pattern Phase 1 used repeatedly
  (dotnet-sdk cask, docker healthcheck, etc.).
- App Proxy requests are NOT signature-verified by the .NET endpoint in
  this plan (Shopify signs proxied requests with a separate signature
  scheme from webhook HMAC). This is a deliberate, disclosed
  simplification: the endpoint is read-only and non-sensitive (it only
  answers "is this item low stock," never mutates anything), unlike the
  webhook receiver, which does mutate data and must stay HMAC-verified.
  Say this plainly if asked — don't imply the proxy call is verified
  when it isn't.
- Keep it tight: one new backend endpoint, one Liquid section, one JS
  file. No additional storefront features.

---

### Task 1 [CODE]: `GET /api/products/by-inventory-item/{inventoryItemId}` endpoint

**Files:**
- Create: `src/InventorySync.Api/Models/LowStockStatus.cs`
- Modify: `src/InventorySync.Api/Controllers/ProductsController.cs`
- Create: `tests/InventorySync.Api.Tests/LowStockStatusEndpointTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`, `Product` (existing, from Phase 1).
- Produces: `GET /api/products/by-inventory-item/{inventoryItemId:long}`
  returning `LowStockStatus` (camelCase JSON, ASP.NET Core's default):
  `{ inventoryItemId, tracked, quantity, lowStockThreshold, isLowStock }`.
  Phase 2's Liquid/JS work (Task 6) depends on this exact shape and route.
  This task has no dependency on Shopify/tunnel/anything else — it can
  run standalone against the existing InMemory test setup.

This task has no Shopify dependency at all — dispatch it exactly like a
Phase 1 task, independent of everything else in this plan.

- [x] **Step 1: Write the failing tests**

`tests/InventorySync.Api.Tests/LowStockStatusEndpointTests.cs`:
```csharp
using System.Net.Http.Json;
using InventorySync.Api.Data;
using InventorySync.Api.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InventorySync.Api.Tests;

public class LowStockStatusEndpointTests : IClassFixture<InMemoryApiFactory>
{
    private readonly HttpClient _client;
    private readonly InMemoryApiFactory _factory;

    public LowStockStatusEndpointTests(InMemoryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ReturnsIsLowStockTrue_WhenQuantityAtOrBelowThreshold()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Products.Add(new Product
            {
                ShopifyInventoryItemId = 555111,
                Title = "Low Item",
                Sku = "LOW-ITEM",
                Quantity = 2,
                LowStockThreshold = 5
            });
            db.SaveChanges();
        }

        var result = await _client.GetFromJsonAsync<LowStockStatus>(
            "/api/products/by-inventory-item/555111");

        Assert.NotNull(result);
        Assert.True(result!.Tracked);
        Assert.True(result.IsLowStock);
        Assert.Equal(2, result.Quantity);
    }

    [Fact]
    public async Task ReturnsIsLowStockFalse_WhenQuantityAboveThreshold()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Products.Add(new Product
            {
                ShopifyInventoryItemId = 555222,
                Title = "High Item",
                Sku = "HIGH-ITEM",
                Quantity = 50,
                LowStockThreshold = 5
            });
            db.SaveChanges();
        }

        var result = await _client.GetFromJsonAsync<LowStockStatus>(
            "/api/products/by-inventory-item/555222");

        Assert.NotNull(result);
        Assert.True(result!.Tracked);
        Assert.False(result.IsLowStock);
    }

    [Fact]
    public async Task ReturnsTrackedFalse_ForUnknownInventoryItemId_NotNotFound()
    {
        var response = await _client.GetAsync("/api/products/by-inventory-item/999999999");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<LowStockStatus>();
        Assert.NotNull(result);
        Assert.False(result!.Tracked);
        Assert.False(result.IsLowStock);
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/InventorySync.Api.Tests --filter LowStockStatusEndpointTests
```
Expected: FAIL to compile — `LowStockStatus` doesn't exist yet.

- [x] **Step 3: Write the response model**

`src/InventorySync.Api/Models/LowStockStatus.cs`:
```csharp
namespace InventorySync.Api.Models;

public record LowStockStatus(
    long InventoryItemId,
    bool Tracked,
    int? Quantity,
    int? LowStockThreshold,
    bool IsLowStock);
```

- [x] **Step 4: Add the endpoint**

Add to `src/InventorySync.Api/Controllers/ProductsController.cs` (inside
the existing `ProductsController` class, alongside the other methods):
```csharp
    [HttpGet("by-inventory-item/{inventoryItemId:long}")]
    public async Task<ActionResult<LowStockStatus>> GetByInventoryItem(long inventoryItemId)
    {
        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ShopifyInventoryItemId == inventoryItemId);

        if (product is null)
        {
            return new LowStockStatus(inventoryItemId, Tracked: false, Quantity: null,
                LowStockThreshold: null, IsLowStock: false);
        }

        return new LowStockStatus(
            inventoryItemId,
            Tracked: true,
            Quantity: product.Quantity,
            LowStockThreshold: product.LowStockThreshold,
            IsLowStock: product.Quantity <= product.LowStockThreshold);
    }
```
(Needs `using InventorySync.Api.Models;` — already present in this file.)

- [x] **Step 5: Run the tests to verify they pass**

```bash
dotnet test tests/InventorySync.Api.Tests --filter LowStockStatusEndpointTests
```
Expected: PASS (all three tests).

- [x] **Step 6: Run the full suite to confirm no regressions**

```bash
dotnet test tests/InventorySync.Api.Tests --filter "FullyQualifiedName!~LowStockReportTests"
```
Expected: PASS.

- [x] **Step 7: Commit**

```bash
git add src/InventorySync.Api/Models/LowStockStatus.cs \
  src/InventorySync.Api/Controllers/ProductsController.cs \
  tests/InventorySync.Api.Tests/LowStockStatusEndpointTests.cs
git commit -m "Add by-inventory-item low-stock status endpoint for storefront badge"
```

---

### Task 2 [INFRA]: Cloudflare Quick Tunnel

**Files:** none (local process/tooling only).

**Interfaces:**
- Produces: a public `https://*.trycloudflare.com` URL forwarding to
  `http://localhost:5072`. Task 4 (App Proxy config) and Task 5
  (webhook subscription) both need this URL. Note it down — it changes
  every time the tunnel restarts (no fixed hostname on the free quick
  tunnel), so Tasks 4/5 will need re-pointing whenever that happens
  during this project.

- [x] **Step 1: Install cloudflared**

```bash
brew install cloudflared
cloudflared --version
```

- [x] **Step 2: Start the API (if not already running)**

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
docker compose ps   # confirm the db container is healthy first
nohup dotnet run --project src/InventorySync.Api > /tmp/inventorysync-api.log 2>&1 &
for i in $(seq 1 30); do curl -sf http://localhost:5072/api/products > /dev/null && break; sleep 1; done
curl -s http://localhost:5072/api/products
```

- [x] **Step 3: Start the tunnel and capture the public URL**

```bash
nohup cloudflared tunnel --url http://localhost:5072 > /tmp/cloudflared.log 2>&1 &
sleep 5
grep -o 'https://[a-zA-Z0-9-]*\.trycloudflare\.com' /tmp/cloudflared.log | head -1
```
Expected: a `https://<random>.trycloudflare.com` URL printed.

- [x] **Step 4: Verify the tunnel actually forwards to the API**

```bash
curl -s https://<the-url-from-step-3>/api/products
```
Expected: same JSON the local `curl` in Step 2 returned.

No commit for this task — nothing in the repo changes.

---

### Task 3 [x] [USER ACTION]: Install Shopify CLI and authenticate — DONE

**Interfaces:**
- Produces: an authenticated `shopify` CLI session (its credential
  persists on disk under the user's home directory), needed by Task 6
  (theme pull/push) and usable by the controller afterward via Bash
  without re-prompting for login.

- [x] **Step 1: Install Shopify CLI**

```bash
npm install -g @shopify/cli @shopify/theme
shopify version
```

- [x] **Step 2: Authenticate (interactive — run this yourself)**

```bash
shopify auth login
```

---

### Task 4 [x] [Corrected from USER ACTION to mostly CODE]: Register the app and configure the App Proxy — DONE

**As actually done — this task's mechanism differs substantially from
what was originally written above.** Legacy custom apps (created
directly from a store's Admin → Settings → Apps → Develop apps) are
disabled for new creation as of January 1, 2026. The current path is
Shopify's **Dev Dashboard**, and choosing "Start with Shopify CLI"
there gives a declarative `shopify.app.toml` config file — version-
controllable, and mostly executable via Bash once the user's CLI login
(Task 3) exists, rather than manual dashboard clicking throughout.

- [x] **Step 1 (you, in Dev Dashboard):** Choose "Build and manage apps
  in your Dev Dashboard" → "Start with Shopify CLI" → gave the app name
  "inventory-sync-proxy" → dashboard displayed the scaffold command.

- [x] **Step 2 (controller, via Bash):** Ran the scaffold command with
  flags to skip prompts and avoid a full Node/Remix template:
  ```bash
  npx @shopify/create-app@latest --name "inventory-sync-proxy" \
    --template none --path shopify-app
  ```
  `--template none` uses Shopify's `shopify-app-template-extension-only`
  template, which — despite the name — still scaffolds a full demo "FAQ"
  app (embedded admin pages, metaobjects, an MCP "app-tools" extension).
  All of that is irrelevant to a config-only proxy app and was removed:
  ```bash
  rm -rf shopify-app/inventory-sync-proxy/.git   # scaffold inits its own git repo
  # flatten shopify-app/inventory-sync-proxy/* up to shopify-app/
  rm -rf shopify-app/extensions shopify-app/shared shopify-app/mcp.json \
    shopify-app/.graphqlrc.js shopify-app/pnpm-workspace.yaml \
    shopify-app/vite.config.ts shopify-app/CHANGELOG.md shopify-app/SECURITY.md
  # also removed the now-pointless "workspaces": ["extensions/*"] from package.json
  ```

- [x] **Step 3 (controller, editing `shopify-app/shopify.app.toml`):**
  Removed the FAQ/metaobject/`[sidekick]` blocks (irrelevant demo
  content), and added:
  ```toml
  [webhooks]
  api_version = "2026-10"

    [[webhooks.subscriptions]]
    topics = ["inventory_levels/update"]
    uri = "https://<tunnel>/webhooks/inventory-update"

  [app_proxy]
  url = "https://<tunnel>/api"
  subpath = "inventory"
  prefix = "apps"          # NOTE: the field is `prefix`, not `subpath_prefix`
                            # as originally guessed above — deploy fails
                            # with "[app_proxy.prefix]: Required" otherwise.
  ```
  Also pointed `application_url` and `[auth] redirect_urls` at the
  tunnel (they're otherwise unused — no embedded UI, no OAuth flow ever
  runs — but left as the scaffold's dead `shopify.dev` placeholder felt
  worse than a real, reachable URL).

- [x] **Step 4 (controller, via Bash):** `access_scopes` needed
  `read_inventory` — deploying without it fails with `Missing scope for
  webhook topic: inventory_levels/update (read_inventory)`. Then deploy
  non-interactively:
  ```bash
  cd shopify-app
  npx shopify app deploy --allow-updates --message "Configure App Proxy and inventory_levels/update webhook"
  ```

- [x] **Step 5 (controller, via Bash):** Installed the app on the dev
  store — `shopify app deploy` registers the config but doesn't install
  it on a specific store by itself. Ran `shopify app dev` briefly
  (`--no-update` so it respects the toml's tunnel URL instead of
  generating its own):
  ```bash
  npx shopify app dev --store <dev-store>.myshopify.com --no-update
  ```
  Confirmed via its output (`Access scopes auto-granted: read_inventory`,
  `Using URL: https://<tunnel>/api`) that install + scope grant + proxy
  URL were all correct, then stopped the process — no need to keep it
  running (no extensions to serve live).

- [x] **Step 6 (you, in Dev Dashboard):** Client secret retrieved from
  the app's "Client credentials" section — needed by Task 5.

---

### Task 5 [x] [USER ACTION + mixed]: Configure the real webhook subscription and set the secret — DONE

**Corrected:** the webhook subscription isn't configured separately in
a dashboard "Webhooks" screen — it's the same `[[webhooks.subscriptions]]`
block in `shopify.app.toml` from Task 4, deployed together with the App
Proxy config. Nothing separate to do here on that front.

- [x] **Step 1 (you, in your own terminal — keeps the secret out of
  chat and shell history):**
  ```bash
  read -s "SHOPIFY_WEBHOOK_SECRET?Webhook secret: "; echo
  export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
  dotnet user-secrets set "Shopify:WebhookSecret" "$SHOPIFY_WEBHOOK_SECRET" \
    --project src/InventorySync.Api
  unset SHOPIFY_WEBHOOK_SECRET
  ```
  (Note: zsh's `read -p` means something different — "read from a
  coprocess" — not "show a prompt" like bash. Use the `"VAR?prompt"`
  form shown above instead.)

- [x] **Step 2 (controller, restart the API):**
  ```bash
  lsof -ti:5072 -sTCP:LISTEN | xargs -r kill
  export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
  nohup dotnet run --project src/InventorySync.Api > /tmp/inventorysync-api.log 2>&1 &
  ```

- [x] **Step 3 (verify, together — real end-to-end, not simulated):**
  Needed a real product/variant to test against. Looked one up via the
  local GraphiQL proxy that `shopify app dev` exposes (required
  temporarily adding `read_products` to `access_scopes` — noted in the
  toml as testing-only, not used by the app at runtime):
  ```bash
  curl -s -X POST "http://localhost:3457/graphiql/graphql.json?key=<key>" \
    -H "Content-Type: application/json" \
    -d '{"query":"{ products(first: 5) { edges { node { title variants(first: 3) { edges { node { title inventoryItem { id } } } } } } } }"}'
  ```
  Used "The Complete Snowboard" / "Ice" variant
  (`inventory_item_id 56346223968569`, starting quantity 10). Registered
  it via the API:
  ```bash
  curl -s -X POST http://localhost:5072/api/products -H "Content-Type: application/json" \
    -d '{"shopifyInventoryItemId":56346223968569,"title":"Complete Snowboard - Ice","sku":"SNOWBOARD-ICE","quantity":10,"lowStockThreshold":5}'
  ```
  Then the user changed that variant's inventory to 5 in Shopify Admin
  (Products → Inventory page, or the variant's own page — the exact
  screen location shifted during testing; either works). Confirmed via
  the API log and `GET /api/products/by-inventory-item/56346223968569`:
  the real webhook fired, `InventoryLog` recorded
  `PreviousQuantity: 10, NewQuantity: 5`, and `isLowStock` flipped to
  `true`. Full real chain proven: Shopify Admin edit → real webhook →
  tunnel → HMAC verified with the real secret → SQL Server updated.

No application-code commit needed for the secret itself (lives only in
`dotnet user-secrets`, outside the repo) — but `shopify-app/` (the
`shopify.app.toml` and its supporting files) should be committed, since
it's config-as-code for this project, not a secret.

---

### Task 6 [CODE-ish, executed via Shopify CLI]: Liquid "Low Stock Alert" badge

Requires Task 3's authenticated CLI and Task 1's endpoint deployed.

**Files:**
- Create (in a pulled theme directory, path depends on where you pull
  to — suggested: `theme/` at the repo root): `sections/low-stock-alert.liquid`,
  `assets/low-stock-alert.js`
- Modify: `templates/product.json` (or your theme's actual product
  template file — inspect it first; Online Store 2.0 themes structure
  this slightly differently from theme to theme)

**Interfaces:**
- Consumes: Task 1's `GET /api/products/by-inventory-item/{id}` via the
  App Proxy path configured in Task 4 (`/apps/inventory/products/by-inventory-item/{id}`).

- [ ] **Step 1: Pull the current theme into the repo**

```bash
mkdir -p theme
cd theme
shopify theme pull --store <your-dev-store>.myshopify.com
cd ..
```

- [ ] **Step 2: Inspect the actual product template**

```bash
cat theme/templates/product.json
```
Read its actual `sections`/`order` structure before editing — themes
differ here. Identify where the main product-info section is, so the
new section can be inserted directly after it.

- [ ] **Step 3: Write the section**

`theme/sections/low-stock-alert.liquid`:
```liquid
{% liquid
  assign variant = product.selected_or_first_available_variant
  assign inventory_item_id = variant.inventory_item_id
%}

<div
  class="low-stock-alert"
  data-low-stock-alert
  data-inventory-item-id="{{ inventory_item_id }}"
  hidden
>
  <p class="low-stock-alert__text">Low stock — order soon!</p>
</div>

{{ 'low-stock-alert.js' | asset_url | script_tag }}

{% schema %}
{
  "name": "Low Stock Alert",
  "settings": []
}
{% endschema %}
```

- [ ] **Step 4: Write the JS**

`theme/assets/low-stock-alert.js`:
```javascript
document.addEventListener('DOMContentLoaded', () => {
  document.querySelectorAll('[data-low-stock-alert]').forEach(async (el) => {
    const inventoryItemId = el.dataset.inventoryItemId;
    if (!inventoryItemId) return;

    try {
      const response = await fetch(
        `/apps/inventory/products/by-inventory-item/${inventoryItemId}`
      );
      if (!response.ok) return;

      const data = await response.json();
      if (data.isLowStock) {
        el.hidden = false;
      }
    } catch (error) {
      console.error('Low stock alert check failed:', error);
    }
  });
});
```

- [ ] **Step 5: Wire the section into the product template**

Edit `theme/templates/product.json`: add a `"low-stock-alert"` entry to
the `sections` object (`"type": "low-stock-alert"`) and insert its key
into the `order` array immediately after the main product section.
Match the exact JSON shape already used by the template's other
section entries.

- [ ] **Step 6: Preview live**

```bash
cd theme
shopify theme dev --store <your-dev-store>.myshopify.com
```
Open the printed preview URL, navigate to a product page whose variant
maps to a `Product` row with low stock (create one via
`POST /api/products` if needed, using that variant's real
`inventory_item_id`), and confirm the badge appears. Check one high-
stock product too, and confirm the badge stays hidden.

- [ ] **Step 7: Push and commit**

```bash
cd theme
shopify theme push --store <your-dev-store>.myshopify.com
cd ..
git add theme/sections/low-stock-alert.liquid theme/assets/low-stock-alert.js \
  theme/templates/product.json
git commit -m "Add Low Stock Alert Liquid section, wired to the by-inventory-item API via App Proxy"
```

---

### Task 7 [USER ACTION, verification]: End-to-end manual test

- [ ] **Step 1:** Ensure a product row exists (via `POST /api/products`)
  whose `shopifyInventoryItemId` matches a real variant's
  `inventory_item_id` on a product in the dev store, with `quantity`
  above its `lowStockThreshold` (so the badge starts hidden).
- [ ] **Step 2:** Load that product's storefront page (via
  `shopify theme dev`'s preview URL). Confirm no badge is visible.
- [ ] **Step 3:** In the dev store's Shopify Admin, edit that variant's
  inventory to a quantity at or below the threshold.
- [ ] **Step 4:** Confirm (via `/tmp/inventorysync-api.log` and/or
  `curl http://localhost:5072/api/products`) that the real Shopify
  webhook fired and the `Product` row's `Quantity` updated.
- [ ] **Step 5:** Reload the storefront product page. Confirm the "Low
  stock — order soon!" badge now appears.

Record the actual result (pass/fail, and exact commands run) for the
README — this is the Phase 2 equivalent of Phase 1's documented manual
stored-procedure verification.

---

### Task 8 [CODE, docs]: README + PLAN.md update

**Files:**
- Modify: `README.md`
- Modify: `PLAN.md`

- [ ] **Step 1:** Update `README.md`: add a "Phase 2" section describing
  what's built (App Proxy, real webhook, Liquid badge), the honest
  caveats (dev tunnel not a deployment; App Proxy calls not
  signature-verified, disclosed as a deliberate simplification since
  the endpoint is read-only; quick tunnel URLs are ephemeral so the
  live demo requires the tunnel/App Proxy config to be in sync), and
  the exact manual end-to-end verification steps/result from Task 7.
- [ ] **Step 2:** Check off Phase 2 items in `PLAN.md` that were actually
  completed; leave anything not done unchecked.
- [ ] **Step 3:** Commit:
  ```bash
  git add README.md PLAN.md
  git commit -m "Document Phase 2 Shopify storefront integration"
  ```

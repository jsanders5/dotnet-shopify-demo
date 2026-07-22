# Day 2 — Shopify Storefront Integration Implementation Plan

> **For agentic workers:** Tasks marked **[CODE]** use
> superpowers:subagent-driven-development (dispatch a fresh implementer
> subagent, review, repeat) exactly like Day 1. Tasks marked **[USER
> ACTION]** involve Shopify's own web UI (Partner Dashboard, store
> admin) or an interactive CLI login tied to the user's account — these
> cannot be dispatched to a subagent (no subagent can complete an OAuth
> login or click through someone else's dashboard) and are done by the
> human, in this session, with the controller providing exact
> instructions and verifying the result afterward. Tasks marked
> **[INFRA]** are local tooling/process setup with no login involved —
> the controller runs these directly via Bash, same as Day 1's
> Colima/Docker setup.

**Goal:** Wire the Day 1 .NET API to a real Shopify development store: a
Liquid "Low Stock Alert" badge on the product page, backed by a live
App Proxy call, driven end-to-end by a real Shopify webhook.

**Architecture:** See
`docs/superpowers/specs/2026-07-22-day2-shopify-storefront-design.md`
for the full design and reasoning. Summary: Cloudflare Quick Tunnel
exposes the local API; a Shopify custom app (Partner Dashboard) holds
the App Proxy config and the real webhook subscription; a new .NET
endpoint answers "is this product low stock"; a Liquid section + small
JS fetch renders the badge.

## Global Constraints

- Never overstate what's built (CLAUDE.md's hard rule, carried forward
  from Day 1): the tunnel is a dev tunnel, not a deployment; the custom
  app exists only for App Proxy/webhook config, not as a published app.
- Real secrets (the app's client secret / webhook signing secret) are
  set via `dotnet user-secrets` directly by the human in their own
  terminal — never pasted into chat, never committed.
- Exact Shopify Partner Dashboard UI labels/steps below are written
  from current knowledge of Shopify's App Proxy and webhook
  configuration screens, but Shopify's UI changes over time. If a step
  doesn't match what's actually on screen, adapt to what's there and
  correct this plan doc afterward — same pattern Day 1 used repeatedly
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
- Consumes: `AppDbContext`, `Product` (existing, from Day 1).
- Produces: `GET /api/products/by-inventory-item/{inventoryItemId:long}`
  returning `LowStockStatus` (camelCase JSON, ASP.NET Core's default):
  `{ inventoryItemId, tracked, quantity, lowStockThreshold, isLowStock }`.
  Day 2's Liquid/JS work (Task 6) depends on this exact shape and route.
  This task has no dependency on Shopify/tunnel/anything else — it can
  run standalone against the existing InMemory test setup.

This task has no Shopify dependency at all — dispatch it exactly like a
Day 1 task, independent of everything else in this plan.

- [ ] **Step 1: Write the failing tests**

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

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/InventorySync.Api.Tests --filter LowStockStatusEndpointTests
```
Expected: FAIL to compile — `LowStockStatus` doesn't exist yet.

- [ ] **Step 3: Write the response model**

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

- [ ] **Step 4: Add the endpoint**

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

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test tests/InventorySync.Api.Tests --filter LowStockStatusEndpointTests
```
Expected: PASS (all three tests).

- [ ] **Step 6: Run the full suite to confirm no regressions**

```bash
dotnet test tests/InventorySync.Api.Tests --filter "FullyQualifiedName!~LowStockReportTests"
```
Expected: PASS.

- [ ] **Step 7: Commit**

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

- [ ] **Step 1: Install cloudflared**

```bash
brew install cloudflared
cloudflared --version
```

- [ ] **Step 2: Start the API (if not already running)**

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
docker compose ps   # confirm the db container is healthy first
nohup dotnet run --project src/InventorySync.Api > /tmp/inventorysync-api.log 2>&1 &
for i in $(seq 1 30); do curl -sf http://localhost:5072/api/products > /dev/null && break; sleep 1; done
curl -s http://localhost:5072/api/products
```

- [ ] **Step 3: Start the tunnel and capture the public URL**

```bash
nohup cloudflared tunnel --url http://localhost:5072 > /tmp/cloudflared.log 2>&1 &
sleep 5
grep -o 'https://[a-zA-Z0-9-]*\.trycloudflare\.com' /tmp/cloudflared.log | head -1
```
Expected: a `https://<random>.trycloudflare.com` URL printed.

- [ ] **Step 4: Verify the tunnel actually forwards to the API**

```bash
curl -s https://<the-url-from-step-3>/api/products
```
Expected: same JSON the local `curl` in Step 2 returned.

No commit for this task — nothing in the repo changes.

---

### Task 3 [USER ACTION]: Install Shopify CLI and authenticate

**Interfaces:**
- Produces: an authenticated `shopify` CLI session (its credential
  persists on disk under the user's home directory), needed by Task 6
  (theme pull/push) and usable by the controller afterward via Bash
  without re-prompting for login.

- [ ] **Step 1: Install Shopify CLI**

```bash
npm install -g @shopify/cli @shopify/theme
shopify version
```

- [ ] **Step 2: Authenticate (interactive — run this yourself)**

Shopify CLI login opens a browser window for you to approve — this is
an OAuth-style login tied to your own Shopify Partner account, so it
needs to be you completing it interactively, not something run
unattended:
```bash
shopify auth login
```
Follow the browser prompt. Once done, the CLI session persists, and
subsequent `shopify` commands (including ones run for you later in
this plan) won't need you to log in again.

---

### Task 4 [USER ACTION]: Register the custom app and configure the App Proxy

Requires Task 2's tunnel URL. Do this in the Shopify Partner Dashboard
(partners.shopify.com), in your Partner organization:

- [ ] **Step 1:** Apps → Create app → choose the manual/custom app
  path (not a CLI-scaffolded template — this app exists only to hold
  configuration, not to run any code of its own).
- [ ] **Step 2:** Name it something like "Inventory Sync Proxy" (internal
  name only, never customer-facing).
- [ ] **Step 3:** In the app's configuration, find **App proxy** (under
  "App setup" or "Configuration," depending on current Shopify UI —
  adapt to whatever's actually there) and set:
  - Subpath prefix: `apps`
  - Subpath: `inventory`
  - Proxy URL: `<tunnel-url-from-task-2>/api`

  (Reasoning: a storefront request to
  `/apps/inventory/products/by-inventory-item/{id}` gets the
  `/apps/inventory` prefix stripped by Shopify and the remainder
  forwarded to `<Proxy URL><remainder>` — i.e.
  `<tunnel>/api/products/by-inventory-item/{id}`, which matches Task
  1's route exactly.)
- [ ] **Step 4:** Save, then install the app on your development store
  (Partner Dashboard should offer an install link, or "Select store" →
  your dev store → install).
- [ ] **Step 5:** Note the app's **Client secret** (in API
  credentials/App setup) — this is the value Task 5 needs as the real
  webhook signing secret. Don't paste it into chat; you'll set it
  directly via a terminal command in Task 5.

---

### Task 5 [USER ACTION + mixed]: Configure the real webhook subscription and set the secret

Requires Task 2's tunnel URL and Task 4's client secret.

- [ ] **Step 1 (you, in Partner Dashboard):** In the same app's
  configuration, find **Webhooks** and add a subscription:
  - Topic: `inventory_levels/update`
  - URL: `<tunnel-url-from-task-2>/webhooks/inventory-update`
  - API version: current stable (whatever the dashboard defaults to)

- [ ] **Step 2 (you, in your own terminal — keeps the secret out of
  chat):** Set the real webhook secret via user-secrets, replacing the
  Day 1 test placeholder:
  ```bash
  export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
  dotnet user-secrets set "Shopify:WebhookSecret" "<the-app's-client-secret>" \
    --project src/InventorySync.Api
  ```

- [ ] **Step 3 (restart the API so it picks up the new secret):**
  ```bash
  lsof -ti:5072 -sTCP:LISTEN | xargs -r kill
  export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
  nohup dotnet run --project src/InventorySync.Api > /tmp/inventorysync-api.log 2>&1 &
  ```

- [ ] **Step 4 (verify, together):** In the dev store admin, edit a
  product variant's inventory quantity. Watch `/tmp/inventorysync-api.log`
  for the incoming webhook request, and confirm via
  `curl http://localhost:5072/api/products` that the corresponding
  `Product` row's `Quantity` actually changed. If nothing arrives,
  double check the webhook URL matches the *current* tunnel URL exactly
  (quick tunnel URLs change on restart — see Task 2's note) and that
  the product/variant you edited maps to a `Product` row created via
  the API (Task 1's data, or new rows POSTed for this test).

No application-code commit for this task (config + a local secret, not
tracked in git).

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
README — this is the Day 2 equivalent of Day 1's documented manual
stored-procedure verification.

---

### Task 8 [CODE, docs]: README + PLAN.md update

**Files:**
- Modify: `README.md`
- Modify: `PLAN.md`

- [ ] **Step 1:** Update `README.md`: add a "Day 2" section describing
  what's built (App Proxy, real webhook, Liquid badge), the honest
  caveats (dev tunnel not a deployment; App Proxy calls not
  signature-verified, disclosed as a deliberate simplification since
  the endpoint is read-only; quick tunnel URLs are ephemeral so the
  live demo requires the tunnel/App Proxy config to be in sync), and
  the exact manual end-to-end verification steps/result from Task 7.
- [ ] **Step 2:** Check off Day 2 items in `PLAN.md` that were actually
  completed; leave anything not done unchecked.
- [ ] **Step 3:** Commit:
  ```bash
  git add README.md PLAN.md
  git commit -m "Document Day 2 Shopify storefront integration"
  ```

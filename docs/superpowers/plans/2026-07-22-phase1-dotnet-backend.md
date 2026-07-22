# Phase 1 — .NET Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the ASP.NET Core Web API half of the Shopify Inventory Sync demo: Product/InventoryLog data model, CRUD endpoints, a `GetLowStockProducts` stored procedure called via `FromSqlRaw`, and a Shopify-webhook receiver that verifies the HMAC signature before persisting inventory updates.

**Architecture:** ASP.NET Core 8 Web API (controllers, not minimal APIs) backed by EF Core Code-First against SQL Server. Locally, "SQL Server" is Azure SQL Edge running in a Colima-managed container (arm64-native substitute for Microsoft SQL Server, since the real Linux SQL Server image has no arm64 build — this is an intentional, documented substitution, not a claim of running real SQL Server).

**Tech Stack:** .NET 8 SDK, ASP.NET Core (controllers), EF Core 8 (SqlServer + InMemory providers), xUnit + `Microsoft.AspNetCore.Mvc.Testing`, Colima + `mcr.microsoft.com/azure-sql-edge`.

## Global Constraints

- .NET 8 LTS SDK, C#, nullable reference types enabled (template default).
- EF Core Code-First migrations only, with exactly one hand-written raw-SQL
  artifact: the `GetLowStockProducts` stored procedure (added via
  `migrationBuilder.Sql(...)` in a migration) — this is the one deliberate
  exception to "everything through the EF model," included specifically to
  demonstrate stored-procedure experience per `job-posting.md`.
- Local SQL engine is Azure SQL Edge (`mcr.microsoft.com/azure-sql-edge`) run
  via Colima, not Docker Desktop, not LocalDB (LocalDB is Windows-only; this
  dev machine is Apple Silicon macOS). Every doc/comment that mentions "SQL
  Server" locally must be honest that it's SQL Edge, not the genuine
  Windows/Linux SQL Server engine, even though the T-SQL/EF Core surface is
  the same.
- No Shopify Partner store, no Liquid code, no live webhook wiring to a real
  Shopify store — that is explicitly Phase 2 scope per `PLAN.md`. This plan
  only covers the Phase 1 backend checklist.
- No claim of running under real IIS anywhere (code, comments, README,
  commit messages). Ship on Kestrel with `web.config` generated correctly
  for in-process hosting (per `CLAUDE.md` option 2), and say plainly that it
  was never deployed to an actual IIS instance.
- Repo will be public. Never commit secrets. DB password lives in a
  git-ignored `.env` (for the container) and in `dotnet user-secrets` (for
  the app's connection string) — never in `appsettings.json`.
- Keep it tight: Products has exactly GET-all, GET-by-id, POST, and the
  low-stock report. No PUT/DELETE, no pagination, no auth — none of that is
  in `PLAN.md`'s Phase 1 scope and adding it would dilute a demo that's
  supposed to be small and defensible.

---

### Task 1: Environment setup & solution scaffold

**Files:**
- Create: `InventorySync.sln`
- Create: `src/InventorySync.Api/` (new ASP.NET Core Web API project, controllers)
- Create: `tests/InventorySync.Api.Tests/` (new xUnit project)
- Create: `docker-compose.yml`
- Create: `.env.example`
- Create: `.gitignore`

**Interfaces:**
- Produces: a buildable empty Web API project (`InventorySync.Api`) and an
  empty xUnit test project (`InventorySync.Api.Tests`) referencing it, plus
  a running Azure SQL Edge container reachable at `localhost,1433`. Later
  tasks add code inside these projects.

- [x] **Step 1: Install the .NET 8 SDK** — done as of commit `725bf06`, via
  `dotnet-install.sh` rather than brew (see below)

The `dotnet-sdk`/`dotnet-sdk@8` brew casks install via a `.pkg` that
invokes `sudo`, which needs an interactive terminal — that failed in a
non-interactive shell. Used Microsoft's official no-admin install script
instead (also sidesteps the generic cask's version-drift problem, since
it pins directly to the `8.0` channel):
```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
bash dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet"
echo 'export DOTNET_ROOT="$HOME/.dotnet"' >> ~/.zshrc
echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.zshrc
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
dotnet --version
```
Result: `8.0.423`. If you have working interactive sudo, `brew install
--cask dotnet-sdk@8` is a fine alternative — just don't use the plain
`dotnet-sdk` cask (tracks latest, currently .NET 10).

- [x] **Step 2: Install Colima + docker CLI + compose plugin**

```bash
brew install colima docker docker-compose
mkdir -p ~/.docker/cli-plugins
ln -sfn "$(brew --prefix)/opt/docker-compose/bin/docker-compose" ~/.docker/cli-plugins/docker-compose
docker compose version
```
Expected: `docker compose version` prints a version (confirms the plugin is
wired up as a Docker CLI plugin, not just the standalone binary).

- [x] **Step 3: Start Colima (arm64, no Rosetta needed — Azure SQL Edge has a native arm64 image)**

```bash
colima start --cpu 2 --memory 4 --arch aarch64 --vm-type vz --mount-type virtiofs
docker ps
```
Expected: `docker ps` runs without error (empty list is fine — confirms the
Docker daemon inside Colima is reachable).

- [x] **Step 4: Scaffold the solution and both projects**

```bash
dotnet new sln -n InventorySync
dotnet new webapi --use-controllers -n InventorySync.Api -o src/InventorySync.Api
dotnet new xunit -n InventorySync.Api.Tests -o tests/InventorySync.Api.Tests
dotnet sln InventorySync.sln add src/InventorySync.Api/InventorySync.Api.csproj
dotnet sln InventorySync.sln add tests/InventorySync.Api.Tests/InventorySync.Api.Tests.csproj
dotnet add tests/InventorySync.Api.Tests/InventorySync.Api.Tests.csproj reference src/InventorySync.Api/InventorySync.Api.csproj
```

- [x] **Step 5: Add NuGet packages** — done as of commit `725bf06`, pinned
  to `8.0.29` (see below)

Pin every package explicitly — an unpinned `dotnet add package` resolves
to the newest stable release on nuget.org (currently 10.0.10), which
doesn't support the `net8.0` TFM these projects target and fails restore
with `NU1202`:
```bash
dotnet add src/InventorySync.Api package Microsoft.EntityFrameworkCore.SqlServer --version 8.0.29
dotnet add src/InventorySync.Api package Microsoft.EntityFrameworkCore.Design --version 8.0.29
dotnet tool install --global dotnet-ef --version 8.0.29 || dotnet tool update --global dotnet-ef --version 8.0.29

dotnet add tests/InventorySync.Api.Tests package Microsoft.EntityFrameworkCore.InMemory --version 8.0.29
dotnet add tests/InventorySync.Api.Tests package Microsoft.AspNetCore.Mvc.Testing --version 8.0.29
```
(`dotnet-ef` is a machine-global tool, not pinned per-repo via a
`dotnet-tools.json` manifest — fine for this demo's scope, but it means a
fresh clone needs the tool reinstalled rather than restored automatically.)

- [x] **Step 6: Delete the template's placeholder weather-forecast files**

```bash
rm -f src/InventorySync.Api/WeatherForecast.cs
rm -f src/InventorySync.Api/Controllers/WeatherForecastController.cs
```

- [x] **Step 7: Write `docker-compose.yml`** — done as of commit `725bf06`,
  healthcheck uses a TCP probe (see below), not `sqlcmd`

```yaml
services:
  db:
    image: mcr.microsoft.com/azure-sql-edge:latest
    container_name: inventorysync-db
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "${MSSQL_SA_PASSWORD}"
    ports:
      - "1433:1433"
    volumes:
      - sql_data:/var/opt/mssql
    healthcheck:
      # This azure-sql-edge image ships neither /opt/mssql-tools nor
      # /opt/mssql-tools18 (no sqlcmd binary at all, confirmed via dpkg -l
      # inside the running container), so a sqlcmd-based healthcheck isn't
      # possible against this image. Falls back to a TCP-connect probe —
      # coarser (port open, not "accepts a login and query") but the only
      # option without baked-in SQL client tooling.
      test: ["CMD-SHELL", "bash -c 'cat < /dev/null > /dev/tcp/127.0.0.1/1433' || exit 1"]
      interval: 10s
      timeout: 5s
      retries: 10

volumes:
  sql_data:
```

- [x] **Step 8: Write `.env.example` and a real (git-ignored) `.env`**

`.env.example`:
```
MSSQL_SA_PASSWORD=ChangeMe_Str0ngPassword!
```

Then create your own local `.env` (not committed) with a real strong
password, e.g.:
```bash
cp .env.example .env
```
Edit `.env` and replace the placeholder with a real password meeting Azure
SQL Edge's complexity rules (8+ chars, upper+lower+digit+symbol).

- [x] **Step 9: Write `.gitignore`**

```
bin/
obj/
publish/
.env
*.user
.vs/
```

- [x] **Step 10: Start the DB and verify it's healthy**

```bash
docker compose up -d
docker compose ps
```
Expected: the `db` service shows `healthy` within ~30-60s (Azure SQL Edge
takes a bit to initialize on first run).

- [x] **Step 11: Commit**

```bash
git add InventorySync.sln src/InventorySync.Api tests/InventorySync.Api.Tests \
  docker-compose.yml .env.example .gitignore
git commit -m "Scaffold ASP.NET Core Web API + xUnit test project, Azure SQL Edge via Colima"
```

---

### Task 2: Data layer — Product & InventoryLog models, DbContext, first migration

**Files:**
- Create: `src/InventorySync.Api/Models/Product.cs`
- Create: `src/InventorySync.Api/Models/InventoryLog.cs`
- Create: `src/InventorySync.Api/Data/AppDbContext.cs`
- Modify: `src/InventorySync.Api/Program.cs`
- Modify: `src/InventorySync.Api/appsettings.json`
- Create: `src/InventorySync.Api/Migrations/*` (via `dotnet ef migrations add`)
- Test: `tests/InventorySync.Api.Tests/AppDbContextTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces: `Product` (`Id`, `ShopifyInventoryItemId` (long), `Title`,
  `Sku`, `Quantity` (int), `LowStockThreshold` (int, default 5)),
  `InventoryLog` (`Id`, `ProductId`, `Product?`, `PreviousQuantity`,
  `NewQuantity`, `ReceivedAtUtc`), and `AppDbContext` with `DbSet<Product>
  Products` and `DbSet<InventoryLog> InventoryLogs`. Later tasks depend on
  these exact property names and types.

- [x] **Step 1: Write the failing test for the DbContext**

`tests/InventorySync.Api.Tests/AppDbContextTests.cs`:
```csharp
using InventorySync.Api.Data;
using InventorySync.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InventorySync.Api.Tests;

public class AppDbContextTests
{
    [Fact]
    public async Task CanAddAndRetrieveProduct()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options);
        db.Products.Add(new Product
        {
            ShopifyInventoryItemId = 808950810,
            Title = "Crusher Evo",
            Sku = "CRUSH-EVO-BLK",
            Quantity = 10
        });
        await db.SaveChangesAsync();

        var stored = await db.Products.SingleAsync();
        Assert.Equal("CRUSH-EVO-BLK", stored.Sku);
        Assert.Equal(5, stored.LowStockThreshold); // default
    }
}
```

- [x] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/InventorySync.Api.Tests --filter AppDbContextTests
```
Expected: FAIL to compile — `AppDbContext`, `Product` don't exist yet.

- [x] **Step 3: Write the models**

`src/InventorySync.Api/Models/Product.cs`:
```csharp
namespace InventorySync.Api.Models;

public class Product
{
    public int Id { get; set; }
    public required long ShopifyInventoryItemId { get; set; }
    public required string Title { get; set; }
    public required string Sku { get; set; }
    public int Quantity { get; set; }
    public int LowStockThreshold { get; set; } = 5;
}
```

`src/InventorySync.Api/Models/InventoryLog.cs`:
```csharp
namespace InventorySync.Api.Models;

public class InventoryLog
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int PreviousQuantity { get; set; }
    public int NewQuantity { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}
```

- [x] **Step 4: Write the DbContext**

`src/InventorySync.Api/Data/AppDbContext.cs`:
```csharp
using InventorySync.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace InventorySync.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryLog> InventoryLogs => Set<InventoryLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>()
            .HasIndex(p => p.ShopifyInventoryItemId)
            .IsUnique();
    }
}
```

- [x] **Step 5: Run the test to verify it passes**

```bash
dotnet test tests/InventorySync.Api.Tests --filter AppDbContextTests
```
Expected: PASS.

- [x] **Step 6: Wire the DbContext into `Program.cs` and configuration**

`src/InventorySync.Api/Program.cs` (replace the generated file's contents):
```csharp
using InventorySync.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposes the implicit Program class so WebApplicationFactory<Program> can
// find it in tests (needed because Program.cs uses top-level statements).
public partial class Program { }
```

Add an empty placeholder in `src/InventorySync.Api/appsettings.json` (do
not put a real password here — this is committed to a public repo):
```json
{
  "ConnectionStrings": {
    "DefaultConnection": ""
  },
  "Shopify": {
    "WebhookSecret": ""
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

- [x] **Step 7: Set the real local connection string via user-secrets (not committed)**

```bash
dotnet user-secrets init --project src/InventorySync.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Server=localhost,1433;Database=InventorySync;User Id=sa;Password=<the password you put in .env>;TrustServerCertificate=True" \
  --project src/InventorySync.Api
```

- [x] **Step 8: Create and apply the first migration**

```bash
dotnet ef migrations add InitialCreate --project src/InventorySync.Api
dotnet ef database update --project src/InventorySync.Api
```
Expected: migration files appear under
`src/InventorySync.Api/Migrations/`, and the command completes without
error (confirms it can reach the Azure SQL Edge container and create the
`Products`/`InventoryLogs` tables).

- [x] **Step 9: Commit**

```bash
git add src/InventorySync.Api tests/InventorySync.Api.Tests
git commit -m "Add Product/InventoryLog models, AppDbContext, and initial migration"
```

---

### Task 3: Products CRUD endpoints (GET all, GET by id, POST)

**Files:**
- Create: `src/InventorySync.Api/Controllers/ProductsController.cs`
- Create: `tests/InventorySync.Api.Tests/InMemoryApiFactory.cs`
- Create: `tests/InventorySync.Api.Tests/ProductsControllerTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`, `Product` from [[Task 2]].
- Produces: `InMemoryApiFactory` (a `WebApplicationFactory<Program>` that
  swaps `AppDbContext` to EF Core's InMemory provider) — Task 6 reuses this
  same factory for the webhook endpoint tests.

- [x] **Step 1: Write the failing tests**

`tests/InventorySync.Api.Tests/InMemoryApiFactory.cs`:
```csharp
using InventorySync.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InventorySync.Api.Tests;

public class InMemoryApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shopify:WebhookSecret"] = "test-webhook-secret"
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null) services.Remove(descriptor);

            // Generate the name once, outside the options lambda: AddDbContext
            // registers DbContextOptions with scoped lifetime, so a lambda
            // re-evaluates once per DI scope (once per HTTP request under
            // WebApplicationFactory). Generating the GUID inside the lambda
            // gave every request its own empty in-memory database, so a POST
            // followed by a GET in the same test always 404'd — found and
            // fixed during Task 3's implementation.
            var dbName = $"TestDb-{Guid.NewGuid()}";
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(dbName));
        });
    }
}
```

`tests/InventorySync.Api.Tests/ProductsControllerTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using InventorySync.Api.Models;
using Xunit;

namespace InventorySync.Api.Tests;

public class ProductsControllerTests : IClassFixture<InMemoryApiFactory>
{
    private readonly HttpClient _client;

    public ProductsControllerTests(InMemoryApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Create_ThenGetById_ReturnsSameProduct()
    {
        var newProduct = new Product
        {
            ShopifyInventoryItemId = 808950810,
            Title = "Crusher Evo",
            Sku = "CRUSH-EVO-BLK",
            Quantity = 10
        };

        var createResponse = await _client.PostAsJsonAsync("/api/products", newProduct);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<Product>();
        Assert.NotNull(created);

        var getResponse = await _client.GetAsync($"/api/products/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var fetched = await getResponse.Content.ReadFromJsonAsync<Product>();
        Assert.Equal(newProduct.Sku, fetched!.Sku);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_ForUnknownId()
    {
        var response = await _client.GetAsync("/api/products/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_IncludesCreatedProduct()
    {
        var newProduct = new Product
        {
            ShopifyInventoryItemId = 111222333,
            Title = "Push Active",
            Sku = "PUSH-ACT-GRY",
            Quantity = 20
        };
        await _client.PostAsJsonAsync("/api/products", newProduct);

        var all = await _client.GetFromJsonAsync<List<Product>>("/api/products");

        Assert.NotNull(all);
        Assert.Contains(all!, p => p.Sku == "PUSH-ACT-GRY");
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/InventorySync.Api.Tests --filter ProductsControllerTests
```
Expected: FAIL to compile — `ProductsController` doesn't exist yet.

- [x] **Step 3: Write the controller**

`src/InventorySync.Api/Controllers/ProductsController.cs`:
```csharp
using InventorySync.Api.Data;
using InventorySync.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySync.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProductsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Product>>> GetAll()
    {
        return await _db.Products.AsNoTracking().ToListAsync();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Product>> GetById(int id)
    {
        var product = await _db.Products.FindAsync(id);
        return product is null ? NotFound() : product;
    }

    [HttpPost]
    public async Task<ActionResult<Product>> Create(Product product)
    {
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }
}
```

- [x] **Step 4: Run the tests to verify they pass**

```bash
dotnet test tests/InventorySync.Api.Tests --filter ProductsControllerTests
```
Expected: PASS (all three tests).

- [x] **Step 5: Commit**

```bash
git add src/InventorySync.Api/Controllers/ProductsController.cs tests/InventorySync.Api.Tests
git commit -m "Add Products CRUD endpoints (GET all, GET by id, POST)"
```

---

### Task 4: Low-stock stored procedure + `FromSqlRaw` endpoint

**Files:**
- Create: `src/InventorySync.Api/Migrations/*_AddGetLowStockProductsProcedure.cs` (via `dotnet ef migrations add`)
- Modify: `src/InventorySync.Api/Controllers/ProductsController.cs`

**Interfaces:**
- Consumes: `AppDbContext`, `Product`, `ProductsController` from [[Task 2]]
  and [[Task 3]].
- Produces: `GET /api/products/low-stock`, backed by the real
  `dbo.GetLowStockProducts` stored procedure.

**Note on testing this task:** EF Core's InMemory provider cannot execute
`FromSqlRaw`/raw SQL, so this endpoint cannot be covered by the fast
InMemory-backed xUnit suite the way Tasks 3 and 6 are. Standing up a
second `WebApplicationFactory` against a real database just to cover one
endpoint is disproportionate setup/debugging risk for what it proves (per
plan review) — instead, this task is verified with a manual `curl` check
against the running container, documented as a reproducible step in the
README (Task 8). This is a deliberate, documented gap in automated
coverage, not an oversight — say so plainly if asked in an interview.

- [x] **Step 1: Create an empty migration and hand-write the stored procedure SQL**

```bash
dotnet ef migrations add AddGetLowStockProductsProcedure --project src/InventorySync.Api
```

Edit the generated migration file's `Up`/`Down` methods:
```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.GetLowStockProducts
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, ShopifyInventoryItemId, Title, Sku, Quantity, LowStockThreshold
    FROM dbo.Products
    WHERE Quantity <= LowStockThreshold;
END");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.GetLowStockProducts");
}
```

```bash
dotnet ef database update --project src/InventorySync.Api
```
Expected: completes without error against the running Azure SQL Edge
container.

- [x] **Step 2: Add the endpoint**

Add to `src/InventorySync.Api/Controllers/ProductsController.cs` (inside
the existing `ProductsController` class, alongside the Task 3 methods):
```csharp
    [HttpGet("low-stock")]
    public async Task<ActionResult<IEnumerable<Product>>> GetLowStock()
    {
        return await _db.Products
            .FromSqlRaw("EXEC dbo.GetLowStockProducts")
            .ToListAsync();
    }
```

- [x] **Step 3: Verify manually against the running container**

```bash
dotnet run --project src/InventorySync.Api &
sleep 3
curl -s -X POST http://localhost:5072/api/products \
  -H "Content-Type: application/json" \
  -d '{"shopifyInventoryItemId":1,"title":"Low","sku":"LOW","quantity":2,"lowStockThreshold":5}'
curl -s -X POST http://localhost:5072/api/products \
  -H "Content-Type: application/json" \
  -d '{"shopifyInventoryItemId":2,"title":"High","sku":"HIGH","quantity":50,"lowStockThreshold":5}'
curl -s http://localhost:5072/api/products/low-stock
kill %1
```
(5072 is this project's configured HTTP port per `launchSettings.json`; adjust if `dotnet run` prints something different.)
Expected: the final `curl` returns a JSON array containing only the
`"sku":"LOW"` product. This is the verification step to record verbatim
in the README (Task 8) as the documented, reproducible way to confirm the
stored procedure works — since it isn't covered by the automated xUnit
suite (see this task's testing note above).

- [x] **Step 4: Commit**

```bash
git add src/InventorySync.Api/Migrations src/InventorySync.Api/Controllers/ProductsController.cs
git commit -m "Add GetLowStockProducts stored procedure and low-stock endpoint via FromSqlRaw"
```

---

### Task 5: Shopify HMAC webhook verifier

**Files:**
- Create: `src/InventorySync.Api/Services/ShopifyHmacVerifier.cs`
- Create: `tests/InventorySync.Api.Tests/ShopifyHmacVerifierTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks (pure, no DB/HTTP dependency).
- Produces: `ShopifyHmacVerifier.IsValid(string sharedSecret, byte[]
  requestBody, string? hmacHeader) : bool`. [[Task 6]] calls this exact
  signature from the webhook controller.

- [x] **Step 1: Write the failing tests**

`tests/InventorySync.Api.Tests/ShopifyHmacVerifierTests.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;
using InventorySync.Api.Services;
using Xunit;

namespace InventorySync.Api.Tests;

public class ShopifyHmacVerifierTests
{
    private const string Secret = "test-shared-secret";

    [Fact]
    public void IsValid_ReturnsTrue_ForCorrectSignature()
    {
        var body = Encoding.UTF8.GetBytes("{\"available\":5}");
        var expectedHmac = ComputeHmac(Secret, body);

        Assert.True(ShopifyHmacVerifier.IsValid(Secret, body, expectedHmac));
    }

    [Fact]
    public void IsValid_ReturnsFalse_ForTamperedBody()
    {
        var originalBody = Encoding.UTF8.GetBytes("{\"available\":5}");
        var hmacForOriginal = ComputeHmac(Secret, originalBody);
        var tamperedBody = Encoding.UTF8.GetBytes("{\"available\":999}");

        Assert.False(ShopifyHmacVerifier.IsValid(Secret, tamperedBody, hmacForOriginal));
    }

    [Fact]
    public void IsValid_ReturnsFalse_ForMissingHeader()
    {
        var body = Encoding.UTF8.GetBytes("{\"available\":5}");

        Assert.False(ShopifyHmacVerifier.IsValid(Secret, body, null));
    }

    private static string ComputeHmac(string secret, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(body));
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/InventorySync.Api.Tests --filter ShopifyHmacVerifierTests
```
Expected: FAIL to compile — `ShopifyHmacVerifier` doesn't exist yet.

- [x] **Step 3: Write the verifier**

`src/InventorySync.Api/Services/ShopifyHmacVerifier.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;

namespace InventorySync.Api.Services;

public static class ShopifyHmacVerifier
{
    public static bool IsValid(string sharedSecret, byte[] requestBody, string? hmacHeader)
    {
        if (string.IsNullOrEmpty(hmacHeader)) return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sharedSecret));
        var computedHash = hmac.ComputeHash(requestBody);

        return CryptographicOperations.FixedTimeEquals(computedHash, SafeBase64Decode(hmacHeader));
    }

    private static byte[] SafeBase64Decode(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return Array.Empty<byte>();
        }
    }
}
```

- [x] **Step 4: Run the tests to verify they pass**

```bash
dotnet test tests/InventorySync.Api.Tests --filter ShopifyHmacVerifierTests
```
Expected: PASS (all three tests).

- [x] **Step 5: Commit**

```bash
git add src/InventorySync.Api/Services tests/InventorySync.Api.Tests
git commit -m "Add Shopify HMAC-SHA256 webhook signature verifier"
```

---

### Task 6: Webhook receiver endpoint

**Files:**
- Create: `src/InventorySync.Api/Models/InventoryUpdatePayload.cs`
- Create: `src/InventorySync.Api/Controllers/WebhooksController.cs`
- Create: `tests/InventorySync.Api.Tests/WebhooksControllerTests.cs`

**Interfaces:**
- Consumes: `ShopifyHmacVerifier.IsValid(...)` from [[Task 5]];
  `AppDbContext`, `Product`, `InventoryLog` from [[Task 2]];
  `InMemoryApiFactory` from [[Task 3]] (already seeds
  `Shopify:WebhookSecret` = `"test-webhook-secret"` into test
  configuration).
- Produces: `POST /webhooks/inventory-update`. This models Shopify's real
  `inventory_levels/update` webhook topic shape (`inventory_item_id`,
  `available`), not a simplified made-up schema — deliberate, since the
  point of this project is to demonstrate the real integration pattern.

- [x] **Step 1: Write the failing tests**

`tests/InventorySync.Api.Tests/WebhooksControllerTests.cs`:
```csharp
using System.Net;
using System.Security.Cryptography;
using System.Text;
using InventorySync.Api.Data;
using InventorySync.Api.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InventorySync.Api.Tests;

public class WebhooksControllerTests : IClassFixture<InMemoryApiFactory>
{
    private const string Secret = "test-webhook-secret";
    private readonly InMemoryApiFactory _factory;

    public WebhooksControllerTests(InMemoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InventoryUpdate_UpdatesQuantityAndLogsChange_ForValidSignature()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Products.Add(new Product
            {
                ShopifyInventoryItemId = 808950810,
                Title = "Crusher Evo",
                Sku = "CRUSH-EVO-BLK",
                Quantity = 10
            });
            db.SaveChanges();
        }

        var body = "{\"inventory_item_id\":808950810,\"available\":3}";
        var signature = ComputeHmac(Secret, Encoding.UTF8.GetBytes(body));

        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/inventory-update")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Shopify-Hmac-Sha256", signature);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = verifyDb.Products.Single(p => p.ShopifyInventoryItemId == 808950810);
        Assert.Equal(3, product.Quantity);
        Assert.Single(verifyDb.InventoryLogs);
    }

    [Fact]
    public async Task InventoryUpdate_ReturnsUnauthorized_ForBadSignature()
    {
        var body = "{\"inventory_item_id\":808950810,\"available\":3}";
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/inventory-update")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Shopify-Hmac-Sha256", "not-a-valid-signature");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string ComputeHmac(string secret, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(body));
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/InventorySync.Api.Tests --filter WebhooksControllerTests
```
Expected: FAIL to compile — `WebhooksController` doesn't exist yet.

- [x] **Step 3: Write the payload model**

`src/InventorySync.Api/Models/InventoryUpdatePayload.cs`:
```csharp
using System.Text.Json.Serialization;

namespace InventorySync.Api.Models;

public record InventoryUpdatePayload(
    [property: JsonPropertyName("inventory_item_id")] long InventoryItemId,
    [property: JsonPropertyName("available")] int Available);
```

- [x] **Step 4: Write the controller**

`src/InventorySync.Api/Controllers/WebhooksController.cs`:
```csharp
using System.Text;
using System.Text.Json;
using InventorySync.Api.Data;
using InventorySync.Api.Models;
using InventorySync.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySync.Api.Controllers;

[ApiController]
[Route("webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public WebhooksController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpPost("inventory-update")]
    public async Task<IActionResult> InventoryUpdate()
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        var secret = _config["Shopify:WebhookSecret"];
        var hmacHeader = Request.Headers["X-Shopify-Hmac-Sha256"].ToString();

        if (string.IsNullOrEmpty(secret) ||
            !ShopifyHmacVerifier.IsValid(secret, Encoding.UTF8.GetBytes(rawBody), hmacHeader))
        {
            return Unauthorized();
        }

        var payload = JsonSerializer.Deserialize<InventoryUpdatePayload>(rawBody);
        if (payload is null) return BadRequest();

        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.ShopifyInventoryItemId == payload.InventoryItemId);
        if (product is null) return NotFound();

        var previousQuantity = product.Quantity;
        product.Quantity = payload.Available;

        _db.InventoryLogs.Add(new InventoryLog
        {
            ProductId = product.Id,
            PreviousQuantity = previousQuantity,
            NewQuantity = payload.Available
        });

        await _db.SaveChangesAsync();
        return Ok();
    }
}
```

- [x] **Step 5: Run the tests to verify they pass**

```bash
dotnet test tests/InventorySync.Api.Tests --filter WebhooksControllerTests
```
Expected: PASS (both tests).

- [x] **Step 6: Run the full test suite once (InMemory-backed tests only; skip the real-DB ones if the container/env var isn't set up in this shell)**

```bash
dotnet test tests/InventorySync.Api.Tests --filter "FullyQualifiedName!~LowStockReportTests"
```
Expected: PASS.

- [x] **Step 7: Commit**

```bash
git add src/InventorySync.Api/Models/InventoryUpdatePayload.cs \
  src/InventorySync.Api/Controllers/WebhooksController.cs tests/InventorySync.Api.Tests
git commit -m "Add Shopify inventory-update webhook receiver with HMAC verification"
```

---

### Task 7: IIS-deployability config (Kestrel + web.config, no live IIS)

**Files:**
- Modify: `src/InventorySync.Api/InventorySync.Api.csproj`
- Create: `publish/` (generated by `dotnet publish`, git-ignored)

**Interfaces:**
- Consumes: the completed API project from Tasks 1-6.
- Produces: a `publish/web.config` demonstrating an in-process IIS hosting
  configuration is present and correct — not evidence of an actual IIS
  deployment, which this plan does not attempt (per `CLAUDE.md`).

- [x] **Step 1: Make the in-process hosting model explicit in the csproj**

Add this property inside the existing `<PropertyGroup>` in
`src/InventorySync.Api/InventorySync.Api.csproj` (alongside `TargetFramework`,
`Nullable`, etc.):
```xml
<AspNetCoreHostingModel>InProcess</AspNetCoreHostingModel>
```

- [x] **Step 2: Publish and inspect the generated web.config**

```bash
dotnet publish src/InventorySync.Api -c Release -o publish
cat publish/web.config
```
Expected: `web.config` exists and contains
`hostingModel="inprocess"` and an `<aspNetCore>` element pointing at
`InventorySync.Api.dll` via the `dotnet` process path. This is the
artifact that proves the app is IIS-deployable; it was not actually run
under IIS on this machine (there's no IIS on macOS to run it under).

- [x] **Step 3: Add `publish/` to `.gitignore` if not already covered**

Confirm `.gitignore` (from Task 1) already excludes it — it does
(`publish/` is already listed). No change needed if so.

- [x] **Step 4: Commit**

```bash
git add src/InventorySync.Api/InventorySync.Api.csproj
git commit -m "Configure explicit in-process hosting model for IIS deployability"
```

---

### Task 8: README + honest documentation, PLAN.md checkbox update

**Files:**
- Create: `README.md`
- Modify: `PLAN.md`

**Interfaces:**
- Consumes: the finished state of Tasks 1-7 (describes what actually
  exists — write this last, after everything above is green).

- [ ] **Step 1: Write `README.md`**

```markdown
# Shopify Inventory Sync & Storefront Alert — .NET backend (Phase 1)

A small ASP.NET Core Web API that models Shopify inventory data and
receives Shopify inventory-update webhooks. Built as a scoped, honest demo
closing real .NET/Shopify skill gaps — see `CLAUDE.md` for why this exists
and what it does and doesn't claim.

## What's here (Phase 1 scope only)

- ASP.NET Core 8 Web API, controller-based
- EF Core Code-First models (`Product`, `InventoryLog`) against SQL Server
- `GET/POST /api/products`, `GET /api/products/{id}`,
  `GET /api/products/low-stock` (backed by a real `GetLowStockProducts`
  stored procedure, called via `FromSqlRaw`)
- `POST /webhooks/inventory-update` — verifies the `X-Shopify-Hmac-Sha256`
  signature before persisting, using the real Shopify
  `inventory_levels/update` payload shape (`inventory_item_id`, `available`)
- Configured for in-process IIS hosting (`web.config` generated on
  publish) — **not actually deployed to IIS**; this dev machine is macOS
  and IIS is Windows-only. See "Honest caveats" below.

## Not here yet (Phase 2 scope, see `PLAN.md`)

- No Shopify Partner store, no Liquid theme code, no live webhook pointed
  at a real Shopify store yet. The webhook receiver is built and tested
  against hand-crafted requests carrying a valid HMAC signature, not
  against a live Shopify send.

## Honest caveats

- **"SQL Server" locally is Azure SQL Edge**, not the genuine SQL Server
  engine. Microsoft's Linux SQL Server container image has no arm64 build,
  so this Apple Silicon dev machine runs Azure SQL Edge instead — it's
  wire-compatible (same T-SQL, same EF Core `SqlServer` provider, stored
  procedures work the same way) but it is a different product, and that
  distinction matters if asked directly.
- **No live IIS deployment.** The app runs on Kestrel locally and is
  configured for IIS's in-process hosting model (see `publish/web.config`
  after running `dotnet publish`), but it has never actually been deployed
  to or run under a real IIS instance.

## Running locally

```bash
cp .env.example .env   # edit in a real password
docker compose up -d
export $(grep -v '^#' .env | xargs)
dotnet ef database update --project src/InventorySync.Api
dotnet run --project src/InventorySync.Api
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
curl -s -X POST http://localhost:5072/api/products -H "Content-Type: application/json" \
  -d '{"shopifyInventoryItemId":1,"title":"Low","sku":"LOW","quantity":2,"lowStockThreshold":5}'
curl -s -X POST http://localhost:5072/api/products -H "Content-Type: application/json" \
  -d '{"shopifyInventoryItemId":2,"title":"High","sku":"HIGH","quantity":50,"lowStockThreshold":5}'
curl -s http://localhost:5072/api/products/low-stock
kill %1
```
Expected: the last call returns only the `"sku":"LOW"` product.
```

- [ ] **Step 2: Check off the completed Phase 1 items in `PLAN.md`**

In `PLAN.md`, under `## Phase 1 — .NET backend`, change each completed line
from `- [ ]` to `- [x]` for every checklist item actually done (scaffold,
SQL Server via Docker, EF Core models + migration, CRUD endpoints, stored
procedure, webhook receiver, basic tests). Leave any not actually done
unchecked.

Also resolve the `## Open questions before starting implementation`
section: replace it with a short note recording the answers actually
used (Azure SQL Edge via Colima; no Shopify Partner store yet — Phase 2;
repo visibility as decided with the user), so the plan file reflects
reality rather than lingering as open questions.

- [ ] **Step 3: Commit**

```bash
git add README.md PLAN.md
git commit -m "Document Phase 1 backend state honestly; check off completed PLAN.md items"
```

# Phase 3 — RAG Product Q&A Implementation Plan

> **For agentic workers:** Tasks marked **[CODE]** use
> superpowers:subagent-driven-development (dispatch a fresh implementer
> subagent, review, repeat), same as Phase 1/2. Tasks marked **[USER
> ACTION]** require the human (an interactive API key signup, or a live
> verification only a person can judge). Task 7 is a hybrid — the
> controller runs the seeding script directly via Bash, but it requires
> Task 6's real API keys to already be set.

**Goal:** A real "Ask about this product" feature on the four Skullcandy
headphone product pages, backed by retrieval-augmented generation:
every answer shown with and without retrieved context, side by side.

**Architecture:** See
`docs/superpowers/specs/2026-07-22-phase3-rag-product-qa-design.md` for
the full design and reasoning. Summary: two new tables hold per-product
guide content and its Voyage AI embeddings (computed once at seed time);
a new endpoint embeds the incoming question, does in-memory cosine
similarity against the stored chunk embeddings, and calls Claude twice
(with and without the retrieved context); a Liquid block on the product
page renders both answers plus the retrieved source text.

## Global Constraints

- Two new secrets: `Anthropic:ApiKey`, `VoyageAi:ApiKey` — both via
  `dotnet user-secrets`, never committed, never pasted into chat.
- Automated tests cover the endpoint's own logic (retrieval ranking,
  404/400 handling, response shape) using fake `IVoyageEmbeddingClient`/
  `IClaudeAnswerClient` implementations — never real network calls. The
  real Anthropic/Voyage integration is verified manually (real cost/
  latency per call), same disclosed pattern as the stored procedure and
  the Phase 2 webhook.
- Guide content (`seed-data/product-guides.json`) is original writing
  based on real public facts from Skullcandy's own product-help pages —
  not copied verbatim. Each product's "no ANC" or "has ANC" status is
  stated explicitly in its own chunk, not left implicit — this is what
  makes the ANC trap-question demo (see the design spec) actually work:
  without an explicit statement, retrieval has nothing clearly on-topic
  to surface for a non-ANC product asked about noise cancelling.
- No vector DB, no admin UI for guide content, no chat history/multi-
  turn, no streaming, no rate limiting — all deliberate, disclosed scope
  boundaries per the design spec, not oversights.
- Keep the existing `ProductsController` route conventions: the new
  route is `POST api/products/{shopifyProductId:long}/ask`, consistent
  with the existing `by-inventory-item`/`by-variant`/`low-stock` pattern
  on the same controller.

---

### Task 1 [CODE]: Content model + migration

**Files:**
- Create: `src/InventorySync.Api/Models/ProductGuide.cs`
- Create: `src/InventorySync.Api/Models/ProductGuideChunk.cs`
- Modify: `src/InventorySync.Api/Data/AppDbContext.cs`
- Test: `tests/InventorySync.Api.Tests/ProductGuideModelTests.cs`

**Interfaces:**
- Produces: `ProductGuide` (`Id`, `ShopifyProductId` long, `Title`
  string, `Chunks` list) and `ProductGuideChunk` (`Id`,
  `ProductGuideId`, `ProductGuide?` nav, `Content` string, `Embedding`
  string — a JSON-serialized `float[]`). `AppDbContext` exposes
  `DbSet<ProductGuide> ProductGuides` and
  `DbSet<ProductGuideChunk> ProductGuideChunks`. Later tasks (3, 5, 7)
  depend on these exact names/types.

- [ ] **Step 1: Write the failing test**

`tests/InventorySync.Api.Tests/ProductGuideModelTests.cs`:
```csharp
using InventorySync.Api.Data;
using InventorySync.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InventorySync.Api.Tests;

public class ProductGuideModelTests
{
    [Fact]
    public async Task CanAddGuideWithChunks_AndLoadThemBack()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options);
        db.ProductGuides.Add(new ProductGuide
        {
            ShopifyProductId = 12345,
            Title = "Test Headphones",
            Chunks = new List<ProductGuideChunk>
            {
                new() { Content = "Chunk one text", Embedding = "[0.1,0.2,0.3]" },
                new() { Content = "Chunk two text", Embedding = "[0.4,0.5,0.6]" }
            }
        });
        await db.SaveChangesAsync();

        var loaded = await db.ProductGuides
            .Include(g => g.Chunks)
            .SingleAsync(g => g.ShopifyProductId == 12345);

        Assert.Equal("Test Headphones", loaded.Title);
        Assert.Equal(2, loaded.Chunks.Count);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
dotnet test tests/InventorySync.Api.Tests --filter ProductGuideModelTests
```
Expected: FAIL to compile — `ProductGuide`/`ProductGuideChunk` don't exist.

- [ ] **Step 3: Write the models**

`src/InventorySync.Api/Models/ProductGuide.cs`:
```csharp
namespace InventorySync.Api.Models;

public class ProductGuide
{
    public int Id { get; set; }
    public required long ShopifyProductId { get; set; }
    public required string Title { get; set; }
    public List<ProductGuideChunk> Chunks { get; set; } = new();
}
```

`src/InventorySync.Api/Models/ProductGuideChunk.cs`:
```csharp
namespace InventorySync.Api.Models;

public class ProductGuideChunk
{
    public int Id { get; set; }
    public int ProductGuideId { get; set; }
    public ProductGuide? ProductGuide { get; set; }
    public required string Content { get; set; }
    // JSON-serialized float[] - the chunk's Voyage AI embedding, computed
    // once at seed time (Task 7), never recomputed per-request (Task 5
    // only embeds the incoming question).
    public required string Embedding { get; set; }
}
```

- [ ] **Step 4: Wire into `AppDbContext`**

Add to `src/InventorySync.Api/Data/AppDbContext.cs` (inside the class,
alongside the existing `DbSet` properties):
```csharp
    public DbSet<ProductGuide> ProductGuides => Set<ProductGuide>();
    public DbSet<ProductGuideChunk> ProductGuideChunks => Set<ProductGuideChunk>();
```
Add to `OnModelCreating` (alongside the existing `Product` index):
```csharp
        modelBuilder.Entity<ProductGuide>()
            .HasIndex(g => g.ShopifyProductId)
            .IsUnique();
```

- [ ] **Step 5: Run the test to verify it passes**

```bash
dotnet test tests/InventorySync.Api.Tests --filter ProductGuideModelTests
```
Expected: PASS.

- [ ] **Step 6: Create and apply the migration**

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
dotnet ef migrations add AddProductGuideTables --project src/InventorySync.Api
dotnet ef database update --project src/InventorySync.Api
```
Expected: both succeed against the running Azure SQL Edge container.

- [ ] **Step 7: Run the full suite to confirm no regressions**

```bash
dotnet test tests/InventorySync.Api.Tests --filter "FullyQualifiedName!~LowStockReportTests"
```

- [ ] **Step 8: Commit**

```bash
git add src/InventorySync.Api/Models/ProductGuide.cs \
  src/InventorySync.Api/Models/ProductGuideChunk.cs \
  src/InventorySync.Api/Data/AppDbContext.cs \
  src/InventorySync.Api/Migrations \
  tests/InventorySync.Api.Tests/ProductGuideModelTests.cs
git commit -m "Add ProductGuide/ProductGuideChunk models and migration"
```

---

### Task 2 [CODE]: Cosine similarity utility

**Files:**
- Create: `src/InventorySync.Api/Services/CosineSimilarity.cs`
- Test: `tests/InventorySync.Api.Tests/CosineSimilarityTests.cs`

**Interfaces:**
- Produces: `CosineSimilarity.Compute(float[] a, float[] b) : double`,
  a pure static function, no DB/HTTP dependency. Task 5 depends on this
  exact signature.

This task is fully independent of Tasks 1/3/4 — dispatch it in parallel
with them if running multiple implementers concurrently.

- [ ] **Step 1: Write the failing tests**

`tests/InventorySync.Api.Tests/CosineSimilarityTests.cs`:
```csharp
using InventorySync.Api.Services;
using Xunit;

namespace InventorySync.Api.Tests;

public class CosineSimilarityTests
{
    [Fact]
    public void Compute_ReturnsOne_ForIdenticalVectors()
    {
        var a = new float[] { 1f, 2f, 3f };
        var result = CosineSimilarity.Compute(a, a);
        Assert.Equal(1.0, result, precision: 6);
    }

    [Fact]
    public void Compute_ReturnsZero_ForOrthogonalVectors()
    {
        var a = new float[] { 1f, 0f };
        var b = new float[] { 0f, 1f };
        var result = CosineSimilarity.Compute(a, b);
        Assert.Equal(0.0, result, precision: 6);
    }

    [Fact]
    public void Compute_RanksMoreSimilarVectorHigher()
    {
        var query = new float[] { 1f, 1f, 0f };
        var close = new float[] { 1f, 0.9f, 0f };
        var far = new float[] { 0f, 0f, 1f };

        var closeSimilarity = CosineSimilarity.Compute(query, close);
        var farSimilarity = CosineSimilarity.Compute(query, far);

        Assert.True(closeSimilarity > farSimilarity);
    }

    [Fact]
    public void Compute_Throws_ForMismatchedLengths()
    {
        var a = new float[] { 1f, 2f };
        var b = new float[] { 1f, 2f, 3f };
        Assert.Throws<ArgumentException>(() => CosineSimilarity.Compute(a, b));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/InventorySync.Api.Tests --filter CosineSimilarityTests
```
Expected: FAIL to compile — `CosineSimilarity` doesn't exist.

- [ ] **Step 3: Write the implementation**

`src/InventorySync.Api/Services/CosineSimilarity.cs`:
```csharp
namespace InventorySync.Api.Services;

public static class CosineSimilarity
{
    public static double Compute(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must be the same length.");

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0) return 0;
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test tests/InventorySync.Api.Tests --filter CosineSimilarityTests
```
Expected: PASS (all four tests).

- [ ] **Step 5: Commit**

```bash
git add src/InventorySync.Api/Services/CosineSimilarity.cs \
  tests/InventorySync.Api.Tests/CosineSimilarityTests.cs
git commit -m "Add cosine similarity utility for RAG chunk retrieval"
```

---

### Task 3 [CODE]: Voyage AI embedding client

**Files:**
- Create: `src/InventorySync.Api/Services/IVoyageEmbeddingClient.cs`
- Create: `src/InventorySync.Api/Services/VoyageEmbeddingClient.cs`
- Create: `tests/InventorySync.Api.Tests/FakeVoyageEmbeddingClient.cs`
- Modify: `src/InventorySync.Api/Program.cs`

**Interfaces:**
- Produces: `IVoyageEmbeddingClient.EmbedAsync(string text,
  CancellationToken ct = default) : Task<float[]>`. Task 5 (the
  endpoint) and Task 7 (seeding) both depend on this exact signature.
  `FakeVoyageEmbeddingClient` (test-only) implements the same interface
  deterministically, for Task 5's tests to use — no real network calls
  in the automated suite, per the Global Constraints.

This task is independent of Tasks 1/2/4 — dispatch in parallel if
running concurrently.

- [ ] **Step 1: Write the interface**

`src/InventorySync.Api/Services/IVoyageEmbeddingClient.cs`:
```csharp
namespace InventorySync.Api.Services;

public interface IVoyageEmbeddingClient
{
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Write the real implementation**

`src/InventorySync.Api/Services/VoyageEmbeddingClient.cs`:
```csharp
using System.Text;
using System.Text.Json;

namespace InventorySync.Api.Services;

public class VoyageEmbeddingClient : IVoyageEmbeddingClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;

    public VoyageEmbeddingClient(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var apiKey = _config["VoyageAi:ApiKey"];
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.voyageai.com/v1/embeddings");
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        var body = JsonSerializer.Serialize(new { input = new[] { text }, model = "voyage-3.5" });
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseBody);
        var embeddingArray = doc.RootElement.GetProperty("data")[0].GetProperty("embedding");
        return embeddingArray.EnumerateArray().Select(e => e.GetSingle()).ToArray();
    }
}
```
Note: `"voyage-3.5"` is Voyage AI's current general-purpose model as of
this plan's writing — if the real API call in Task 6's manual
verification returns a model-not-found error, check
`https://docs.voyageai.com` for the current model name and update this
line (same "adapt to what's actually there" pattern used throughout
this project for external platform specifics).

- [ ] **Step 3: Write the fake for tests**

`tests/InventorySync.Api.Tests/FakeVoyageEmbeddingClient.cs`:
```csharp
using InventorySync.Api.Services;

namespace InventorySync.Api.Tests;

// Deterministic fake: the same input text always produces the same
// vector, and different texts produce different vectors, so similarity
// ranking in tests is meaningful without any real network call.
public class FakeVoyageEmbeddingClient : IVoyageEmbeddingClient
{
    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var random = new Random(text.GetHashCode());
        var vector = new float[8];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)random.NextDouble();
        }
        return Task.FromResult(vector);
    }
}
```

- [ ] **Step 4: Register the real client in `Program.cs`**

Add to `src/InventorySync.Api/Program.cs` (alongside the existing
`AddDbContext` call):
```csharp
builder.Services.AddHttpClient<IVoyageEmbeddingClient, VoyageEmbeddingClient>();
```
(Needs `using InventorySync.Api.Services;` at the top of the file.)

- [ ] **Step 5: Build to confirm everything compiles**

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
dotnet build
```
Expected: 0 warnings, 0 errors. (No dedicated test for this task beyond
compiling — the real client is exercised for real in Task 6's manual
verification; `FakeVoyageEmbeddingClient` is exercised by Task 5's tests.)

- [ ] **Step 6: Commit**

```bash
git add src/InventorySync.Api/Services/IVoyageEmbeddingClient.cs \
  src/InventorySync.Api/Services/VoyageEmbeddingClient.cs \
  tests/InventorySync.Api.Tests/FakeVoyageEmbeddingClient.cs \
  src/InventorySync.Api/Program.cs
git commit -m "Add Voyage AI embedding client (real + test fake)"
```

---

### Task 4 [CODE]: Claude answer client

**Files:**
- Create: `src/InventorySync.Api/Services/IClaudeAnswerClient.cs`
- Create: `src/InventorySync.Api/Services/ClaudeAnswerClient.cs`
- Create: `tests/InventorySync.Api.Tests/FakeClaudeAnswerClient.cs`
- Modify: `src/InventorySync.Api/Program.cs`

**Interfaces:**
- Produces: `IClaudeAnswerClient.AskAsync(string question, string?
  context, CancellationToken ct = default) : Task<string>` — `context`
  is `null` for the ungrounded call, non-null for the grounded call.
  Task 5 depends on this exact signature.

This task is independent of Tasks 1/2/3 — dispatch in parallel if
running concurrently. Both this task and Task 3 modify `Program.cs`;
if run concurrently, resolve the merge by keeping both `AddHttpClient`
registrations.

- [ ] **Step 1: Write the interface**

`src/InventorySync.Api/Services/IClaudeAnswerClient.cs`:
```csharp
namespace InventorySync.Api.Services;

public interface IClaudeAnswerClient
{
    Task<string> AskAsync(string question, string? context, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Write the real implementation**

`src/InventorySync.Api/Services/ClaudeAnswerClient.cs`:
```csharp
using System.Text;
using System.Text.Json;

namespace InventorySync.Api.Services;

public class ClaudeAnswerClient : IClaudeAnswerClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;

    public ClaudeAnswerClient(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    public async Task<string> AskAsync(string question, string? context, CancellationToken cancellationToken = default)
    {
        var apiKey = _config["Anthropic:ApiKey"];
        var prompt = context is null
            ? question
            : $"Use the following product documentation to answer the question. " +
              $"If the documentation doesn't mention a feature, say plainly that " +
              $"this product doesn't have it - don't guess.\n\nDocumentation:\n{context}\n\nQuestion: {question}";

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        var body = JsonSerializer.Serialize(new
        {
            model = "claude-haiku-4-5-20251001",
            max_tokens = 512,
            messages = new[] { new { role = "user", content = prompt } }
        });
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
    }
}
```
Haiku is used deliberately (not Sonnet) — this endpoint makes two Claude
calls per question, and keeping per-question cost/latency low matters
more here than maximizing answer quality for a demo. If real testing in
Task 6 shows Haiku's ungrounded answers are too consistently good to
show a clear contrast, that's worth revisiting, but start here.

- [ ] **Step 3: Write the fake for tests**

`tests/InventorySync.Api.Tests/FakeClaudeAnswerClient.cs`:
```csharp
using InventorySync.Api.Services;

namespace InventorySync.Api.Tests;

public class FakeClaudeAnswerClient : IClaudeAnswerClient
{
    public Task<string> AskAsync(string question, string? context, CancellationToken cancellationToken = default)
    {
        var answer = context is null
            ? $"[ungrounded answer to: {question}]"
            : $"[grounded answer to: {question}, using: {context}]";
        return Task.FromResult(answer);
    }
}
```

- [ ] **Step 4: Register the real client in `Program.cs`**

Add alongside Task 3's registration:
```csharp
builder.Services.AddHttpClient<IClaudeAnswerClient, ClaudeAnswerClient>();
```

- [ ] **Step 5: Build to confirm everything compiles**

```bash
dotnet build
```
Expected: 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/InventorySync.Api/Services/IClaudeAnswerClient.cs \
  src/InventorySync.Api/Services/ClaudeAnswerClient.cs \
  tests/InventorySync.Api.Tests/FakeClaudeAnswerClient.cs \
  src/InventorySync.Api/Program.cs
git commit -m "Add Claude answer client (real + test fake)"
```

---

### Task 5 [CODE]: The `/ask` endpoint

**Files:**
- Create: `src/InventorySync.Api/Models/AskRequest.cs`
- Create: `src/InventorySync.Api/Models/AskResponse.cs`
- Modify: `src/InventorySync.Api/Controllers/ProductsController.cs`
- Modify: `tests/InventorySync.Api.Tests/InMemoryApiFactory.cs`
- Create: `tests/InventorySync.Api.Tests/AskEndpointTests.cs`

**Interfaces:**
- Consumes: `ProductGuide`/`ProductGuideChunk` (Task 1),
  `CosineSimilarity.Compute` (Task 2), `IVoyageEmbeddingClient`/
  `FakeVoyageEmbeddingClient` (Task 3), `IClaudeAnswerClient`/
  `FakeClaudeAnswerClient` (Task 4).
- Produces: `POST /api/products/{shopifyProductId:long}/ask` returning
  `AskResponse { Question, AnswerWithoutContext, AnswerWithContext,
  RetrievedChunks }`.

Depends on Tasks 1-4 all being complete first — dispatch this one last,
after those four land.

- [ ] **Step 1: Write the failing tests**

First, modify `tests/InventorySync.Api.Tests/InMemoryApiFactory.cs` to
substitute the fakes for the real AI clients (find the existing
`ConfigureServices` block and add these registrations alongside the
existing `AppDbContext` swap):
```csharp
            var voyageDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IVoyageEmbeddingClient));
            if (voyageDescriptor is not null) services.Remove(voyageDescriptor);
            services.AddSingleton<IVoyageEmbeddingClient, FakeVoyageEmbeddingClient>();

            var claudeDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IClaudeAnswerClient));
            if (claudeDescriptor is not null) services.Remove(claudeDescriptor);
            services.AddSingleton<IClaudeAnswerClient, FakeClaudeAnswerClient>();
```
(Needs `using InventorySync.Api.Services;` added to this file's usings.)
This is a shared factory change — every existing test using
`InMemoryApiFactory` now also gets fake AI clients, which is harmless
(they're simply never invoked by tests that don't hit `/ask`).

`tests/InventorySync.Api.Tests/AskEndpointTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using InventorySync.Api.Data;
using InventorySync.Api.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InventorySync.Api.Tests;

public class AskEndpointTests : IClassFixture<InMemoryApiFactory>
{
    private readonly HttpClient _client;
    private readonly InMemoryApiFactory _factory;

    public AskEndpointTests(InMemoryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ReturnsBothAnswersAndRetrievedChunks_ForKnownProduct()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ProductGuides.Add(new ProductGuide
            {
                ShopifyProductId = 999111,
                Title = "Ask Test Headphones",
                Chunks = new List<ProductGuideChunk>
                {
                    new() { Content = "Battery lasts 30 hours.", Embedding = "[1,0,0,0,0,0,0,0]" },
                    new() { Content = "Pairing is automatic on first power-on.", Embedding = "[0,1,0,0,0,0,0,0]" }
                }
            });
            db.SaveChanges();
        }

        var response = await _client.PostAsJsonAsync(
            "/api/products/999111/ask", new AskRequest("How long does the battery last?"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<AskResponse>();

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result!.AnswerWithoutContext));
        Assert.False(string.IsNullOrEmpty(result.AnswerWithContext));
        Assert.NotEmpty(result.RetrievedChunks);
    }

    [Fact]
    public async Task ReturnsNotFound_ForUnknownProduct()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/products/888222/ask", new AskRequest("Does this have ANC?"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReturnsBadRequest_ForBlankQuestion()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ProductGuides.Add(new ProductGuide
            {
                ShopifyProductId = 777333,
                Title = "Blank Question Test",
                Chunks = new List<ProductGuideChunk>
                {
                    new() { Content = "Some content.", Embedding = "[1,0,0,0,0,0,0,0]" }
                }
            });
            db.SaveChanges();
        }

        var response = await _client.PostAsJsonAsync(
            "/api/products/777333/ask", new AskRequest(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/InventorySync.Api.Tests --filter AskEndpointTests
```
Expected: FAIL to compile — `AskRequest`/`AskResponse`/the endpoint
don't exist yet.

- [ ] **Step 3: Write the request/response models**

`src/InventorySync.Api/Models/AskRequest.cs`:
```csharp
namespace InventorySync.Api.Models;

public record AskRequest(string Question);
```

`src/InventorySync.Api/Models/AskResponse.cs`:
```csharp
namespace InventorySync.Api.Models;

public record AskResponse(
    string Question,
    string AnswerWithoutContext,
    string AnswerWithContext,
    List<string> RetrievedChunks);
```

- [ ] **Step 4: Update `ProductsController`'s constructor and add the endpoint**

Modify `src/InventorySync.Api/Controllers/ProductsController.cs` — add
the two new constructor dependencies (existing actions are unaffected,
they only use `_db`):
```csharp
using InventorySync.Api.Services;
using System.Text.Json;
// (add alongside existing usings)

    private readonly AppDbContext _db;
    private readonly IVoyageEmbeddingClient _embeddingClient;
    private readonly IClaudeAnswerClient _answerClient;

    public ProductsController(AppDbContext db, IVoyageEmbeddingClient embeddingClient, IClaudeAnswerClient answerClient)
    {
        _db = db;
        _embeddingClient = embeddingClient;
        _answerClient = answerClient;
    }
```

Add the new action (inside the class, alongside the existing methods):
```csharp
    [HttpPost("{shopifyProductId:long}/ask")]
    public async Task<ActionResult<AskResponse>> Ask(long shopifyProductId, AskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question)) return BadRequest();

        var guide = await _db.ProductGuides
            .Include(g => g.Chunks)
            .FirstOrDefaultAsync(g => g.ShopifyProductId == shopifyProductId);
        if (guide is null || guide.Chunks.Count == 0) return NotFound();

        var questionEmbedding = await _embeddingClient.EmbedAsync(request.Question);

        var topChunks = guide.Chunks
            .Select(c => new
            {
                c.Content,
                Similarity = CosineSimilarity.Compute(
                    questionEmbedding, JsonSerializer.Deserialize<float[]>(c.Embedding)!)
            })
            .OrderByDescending(x => x.Similarity)
            .Take(3)
            .Select(x => x.Content)
            .ToList();

        var contextText = string.Join("\n\n", topChunks);

        var answerWithoutContext = await _answerClient.AskAsync(request.Question, null);
        var answerWithContext = await _answerClient.AskAsync(request.Question, contextText);

        return new AskResponse(request.Question, answerWithoutContext, answerWithContext, topChunks);
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test tests/InventorySync.Api.Tests --filter AskEndpointTests
```
Expected: PASS (all three tests).

- [ ] **Step 6: Run the full suite to confirm no regressions**

```bash
dotnet test tests/InventorySync.Api.Tests --filter "FullyQualifiedName!~LowStockReportTests"
```
Expected: PASS — including all pre-existing `ProductsController` tests,
confirming the constructor change didn't break anything.

- [ ] **Step 7: Commit**

```bash
git add src/InventorySync.Api/Models/AskRequest.cs \
  src/InventorySync.Api/Models/AskResponse.cs \
  src/InventorySync.Api/Controllers/ProductsController.cs \
  tests/InventorySync.Api.Tests/InMemoryApiFactory.cs \
  tests/InventorySync.Api.Tests/AskEndpointTests.cs
git commit -m "Add POST /api/products/{id}/ask RAG endpoint"
```

---

### Task 6 [USER ACTION]: Get a Voyage AI key, set both secrets

- [ ] **Step 1 (you):** Sign up for a free Voyage AI account at
  voyageai.com and generate an API key, if you don't already have one.

- [ ] **Step 2 (you, in your own terminal):**
  ```bash
  read -s "ANTHROPIC_KEY?Anthropic API key: "; echo
  export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
  dotnet user-secrets set "Anthropic:ApiKey" "$ANTHROPIC_KEY" --project src/InventorySync.Api
  unset ANTHROPIC_KEY

  read -s "VOYAGE_KEY?Voyage AI API key: "; echo
  dotnet user-secrets set "VoyageAi:ApiKey" "$VOYAGE_KEY" --project src/InventorySync.Api
  unset VOYAGE_KEY
  ```

- [ ] **Step 3 (controller, restart the API to pick up the secrets):**
  ```bash
  lsof -ti:5072 -sTCP:LISTEN | xargs -r kill
  export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
  nohup dotnet run --project src/InventorySync.Api > /tmp/inventorysync-api.log 2>&1 &
  ```

No commit — secrets live only in `dotnet user-secrets`, outside the repo.

---

### Task 7 [Controller runs, needs Task 6's keys]: Seed the guide content

**Files:**
- Create: `seed-data/product-guides.json` (tracked — the actual authored
  content, kept visible/reviewable in the repo)

**Interfaces:**
- Consumes: Task 6's real API keys, Task 1's `ProductGuide`/
  `ProductGuideChunk` tables, a real Voyage AI embeddings call per chunk.
- Produces: 4 populated `ProductGuide` rows (one per headphone) with
  their chunks and real embeddings, ready for Task 5's endpoint and
  Task 9's manual verification.

- [ ] **Step 1: Write the content file**

`seed-data/product-guides.json` — original writing based on real facts
pulled from Skullcandy's product-help pages (see the design spec's
"Content sourcing" section), not copied text. Each product's ANC
status is stated explicitly:

```json
[
  {
    "title": "Skullcandy Crusher 720",
    "shopifyProductId": 10289814470969,
    "chunks": [
      "Controls: a 5-way joystick handles the basics - press up or down to change volume, left or right to skip tracks, and press the center to play, pause, or answer calls. A separate Crusher Wheel adjusts bass intensity independently of the volume level. A custom button toggles spatial audio and cycles through EQ presets. Hold the power button for 1 second to turn the headphones on, or 3 seconds to turn them off.",
      "Pairing and setup: the first time you power on the Crusher 720, it automatically enters pairing mode. Open Bluetooth settings on your phone or computer and select 'Crusher 720' from the list. The headphones support multipoint pairing, so they can stay connected to two devices at once, and Android phones with Google Fast Pair will show an automatic pairing prompt.",
      "Battery and charging: a full charge takes about 2 hours over USB-C and provides up to 65 hours of playback. If the battery runs low, a 10-minute quick charge is enough for more than 4 hours of additional listening.",
      "Noise cancellation: the Crusher 720 does not include active noise cancellation (ANC). Instead, a Stay-Aware mode can be enabled to amplify ambient sound around you without removing the headphones, for situational awareness rather than blocking outside noise.",
      "Troubleshooting: if the headphones become unresponsive or won't pair correctly, hold the center of the joystick for 6 seconds. This clears all paired devices and resets the headphones back into pairing mode."
    ]
  },
  {
    "title": "Skullcandy Crusher 1080 ANC",
    "shopifyProductId": 10289829282105,
    "chunks": [
      "Controls: a 5-way joystick controls volume, track navigation, play/pause, and answering calls. A custom button switches between ANC modes and cycles EQ presets. A Crusher Wheel with about 120 degrees of rotation adjusts bass intensity independently of volume. Hold the power button 1 second to turn on, 3 seconds to turn off.",
      "Pairing and setup: powering on the headphones with no previously paired devices automatically starts pairing mode, announced with a 'Ready to Pair' voice prompt and a pulsing blue LED. Select the headphones from your device's Bluetooth settings to connect. To add a second device via multipoint, hold the center of the joystick for 3 seconds while already connected to a first device.",
      "Battery and charging: with ANC enabled, expect up to 50 hours of playback; with ANC disabled, up to 60 hours. A full charge takes about 45 minutes over USB-C, and a 10-minute quick charge provides more than 4 hours of additional playback.",
      "Noise cancellation: the Crusher 1080 ANC has three noise control modes, switched via the custom button or the Skullcandy app - Quiet mode for maximum active noise cancellation using 6 microphones, Off for natural listening, and Aware mode which lets outside sound through for situational awareness. ANC is off by default and needs to be turned on.",
      "Troubleshooting: to reset, remove the headphones from your device's paired list, power them on, and hold the center of the joystick for 6 seconds. A blue and amber LED will alternate and a voice prompt will announce 'Ready to Pair' once the reset is complete."
    ]
  },
  {
    "title": "Skullcandy Method 360 ANC",
    "shopifyProductId": 10289814503737,
    "chunks": [
      "Controls: a single tap plays or pauses audio, or answers a call. A double tap skips to the next track. A triple tap cycles through ANC modes. Holding a bud for 1 second ends or rejects a call, or can be set to launch Spotify. Most button actions can be reassigned in the Skullcandy app.",
      "Pairing and setup: remove the protective stickers from the charging contacts, then place the earbuds in the case until the LEDs turn yellow or white before removing them. Open your device's Bluetooth settings and select 'METHOD 360 ANC.' For multipoint pairing with a second device, hold either earbud for 3 seconds to re-enter pairing mode.",
      "Battery and charging: with ANC enabled, expect up to 32 hours of total playback (earbuds plus case). The case reaches a full charge in about 1 hour, and a 10-minute quick charge of the earbuds provides more than 2 hours of playback. A low battery warning plays at 15% remaining.",
      "Noise cancellation: Active Noise Cancelling intensity is adjustable via a slider in the Skullcandy app, and a Stay-Aware transparency mode is available for letting ambient sound through when needed.",
      "Troubleshooting: hold a button for 6 seconds to reset the earbuds - a tone plays and the LED flashes blue then red to confirm. Before starting a firmware update through the app, make sure the earbuds have at least 25% battery remaining."
    ]
  },
  {
    "title": "Skullcandy Riff Wireless 2",
    "shopifyProductId": 10289814602041,
    "chunks": [
      "Controls: the Main Function Button (MFB) handles power (1 second hold to turn on, 3 seconds to turn off), pairing, play/pause, answering calls, and rejecting calls (1 second hold). Separate + and - buttons control volume with a single press, or skip tracks with a 1-second hold. Holding both + and - together for 3 seconds resets the headphones.",
      "Pairing and setup: hold the MFB for 1 second until you hear a 'Ready to Pair' voice prompt and see a pulsing red and blue LED, then select the headphones from your device's Bluetooth list. The Riff Wireless 2 supports multipoint pairing with up to two devices, though only one can stream audio at a time.",
      "Battery and charging: a full charge provides up to 34 or more hours of playback. A 10-minute quick charge provides more than 4 hours of additional listening. Charging is via USB-C, with a red LED indicating charging in progress and a green LED indicating a full charge.",
      "Noise cancellation: the Riff Wireless 2 does not include active noise cancellation (ANC). It is an on-ear headphone focused on lightweight comfort and long battery life rather than noise isolation features.",
      "Troubleshooting and calls: an incoming call automatically pauses whatever is streaming and takes priority. If you need to reset the headphones, hold both the + and - buttons together for 3 seconds."
    ]
  }
]
```

- [ ] **Step 2: Confirm the API and secrets are ready**

```bash
curl -s -o /dev/null -w "HTTP %{http_code}\n" http://localhost:5072/api/products
```
Expected: `HTTP 200` (confirms the API from Task 6 Step 3 is up with
the new secrets loaded).

- [ ] **Step 3: Seed via the running API**

For each product in `seed-data/product-guides.json`, `POST` to create
the `ProductGuide` and its chunks with real embeddings. Since there's no
seeding endpoint (deliberately — no CRUD for guide content per the
design spec), this is a one-off script using `Microsoft.Data.SqlClient`
directly against the database, following the exact same pattern as the
earlier one-off data-fix scripts in this project's history (scaffold a
throwaway console project in the scratchpad directory, add the
`Microsoft.Data.SqlClient` package, read `seed-data/product-guides.json`,
call Voyage's embeddings API once per chunk via `HttpClient`, then
`INSERT` the `ProductGuides`/`ProductGuideChunks` rows directly). Get
the real connection string and Voyage key from
`~/.microsoft/usersecrets/*/secrets.json` (already on disk from Task 6)
rather than asking the user to re-paste them. Delete the throwaway
script when done — the tracked artifact worth keeping is
`seed-data/product-guides.json` itself, not the one-off loader.

- [ ] **Step 4: Verify the seed worked**

```bash
curl -s http://localhost:5072/api/products/10289814470969/ask \
  -H "Content-Type: application/json" \
  -d '{"question":"How long does the battery last?"}' | python3 -m json.tool
```
Expected: a real JSON response with non-empty `answerWithoutContext`,
`answerWithContext`, and `retrievedChunks` containing the battery chunk
text. This is a live call to both Voyage and Anthropic - real latency
(a few seconds) and real (small) cost, expected and normal.

- [ ] **Step 5: Commit the content file**

```bash
git add seed-data/product-guides.json
git commit -m "Add original product guide content for the 4 headphone products"
```
(The seeded database rows themselves aren't committed - only the source
content file. Anyone re-running this project from a fresh clone repeats
Task 7's seeding step against their own database.)

---

### Task 8 [CODE-ish, executed via Shopify CLI]: Storefront "Ask" box

**Files:**
- Create: `theme/snippets/ask-about-product.liquid`
- Create: `theme/assets/ask-about-product.js`
- Modify: `theme/sections/main-product.liquid` (add a new block case + schema entry, same pattern as `low_stock_alert`)
- Modify: `theme/templates/product.json` (add the block to `main`'s `blocks`/`block_order`)

**Interfaces:**
- Consumes: Task 5's `POST /apps/inventory/products/{shopifyProductId}/ask`
  endpoint (via the existing App Proxy config from Phase 2 - no new
  Shopify app configuration needed, same proxy path prefix).

- [ ] **Step 1: Write the snippet**

`theme/snippets/ask-about-product.liquid`:
```liquid
{% comment %}
  Rendered as a block inside sections/main-product.liquid, same pattern
  as low-stock-alert. Posts through the existing App Proxy
  (/apps/inventory/... -> the .NET API) to the RAG endpoint added in
  Phase 3. product.id is the Shopify Product ID (not variant), which
  is what ProductGuide.ShopifyProductId is keyed on.
{% endcomment %}
<div class="product-ask" data-product-ask data-product-id="{{ product.id }}">
  <label for="ProductAskInput-{{ section.id }}">Ask about this product</label>
  <input type="text" id="ProductAskInput-{{ section.id }}" data-product-ask-input
    placeholder="e.g. Does this have noise cancelling?" />
  <button type="button" data-product-ask-submit>Ask</button>

  <div data-product-ask-results hidden>
    <div class="product-ask__panel">
      <h4>Without product context</h4>
      <p data-product-ask-answer-without></p>
    </div>
    <div class="product-ask__panel">
      <h4>With product context (RAG)</h4>
      <p data-product-ask-answer-with></p>
      <details>
        <summary>Retrieved source passages</summary>
        <ul data-product-ask-chunks></ul>
      </details>
    </div>
  </div>
</div>

{{ 'ask-about-product.js' | asset_url | script_tag }}
```

- [ ] **Step 2: Write the JS**

`theme/assets/ask-about-product.js`:
```javascript
document.addEventListener('DOMContentLoaded', () => {
  document.querySelectorAll('[data-product-ask]').forEach((container) => {
    const productId = container.dataset.productId;
    const input = container.querySelector('[data-product-ask-input]');
    const button = container.querySelector('[data-product-ask-submit]');
    const results = container.querySelector('[data-product-ask-results]');
    const withoutEl = container.querySelector('[data-product-ask-answer-without]');
    const withEl = container.querySelector('[data-product-ask-answer-with]');
    const chunksEl = container.querySelector('[data-product-ask-chunks]');

    button.addEventListener('click', async () => {
      const question = input.value.trim();
      if (!question) return;

      button.disabled = true;
      button.textContent = 'Asking...';

      try {
        const response = await fetch(`/apps/inventory/products/${productId}/ask`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ question }),
        });

        if (!response.ok) {
          withoutEl.textContent = 'Something went wrong asking that question.';
          withEl.textContent = '';
          results.hidden = false;
          return;
        }

        const data = await response.json();
        withoutEl.textContent = data.answerWithoutContext;
        withEl.textContent = data.answerWithContext;
        chunksEl.innerHTML = '';
        (data.retrievedChunks || []).forEach((chunk) => {
          const li = document.createElement('li');
          li.textContent = chunk;
          chunksEl.appendChild(li);
        });
        results.hidden = false;
      } catch (error) {
        console.error('Product ask failed:', error);
      } finally {
        button.disabled = false;
        button.textContent = 'Ask';
      }
    });
  });
});
```

- [ ] **Step 3: Register the block in `main-product.liquid`**

Add a new `when 'product_ask'` case to the block-rendering loop
(alongside the existing `low_stock_alert` case added in Phase 2):
```liquid
            {%- when 'product_ask' -%}
              {%- render 'ask-about-product' -%}
```
Add to the schema's `blocks` array (block name must stay under 25
characters, same limit that bit the `low_stock_alert` block in Phase 2):
```json
    {
      "type": "product_ask",
      "name": "Product Q&A (RAG)"
    },
```

- [ ] **Step 4: Wire into `templates/product.json`**

Add a `product_ask` entry to `main`'s `blocks` object and insert it into
`block_order`, positioned after `description` (a natural place for a
Q&A box - after the shopper has read the product description):
```json
        "product_ask": {
          "type": "product_ask",
          "settings": {}
        }
```
and in `block_order`, after `"description"`:
```json
        "description",
        "product_ask",
```

- [ ] **Step 5: Preview and verify live**

```bash
cd theme
npx shopify theme dev --store <your-dev-store>.myshopify.com --store-password "<password>"
```
Open one of the four headphone product pages, type a question, click
Ask, and confirm both answer panels populate with real text and the
retrieved chunks list is non-empty. This exercises the real Voyage +
Claude calls end-to-end through the live App Proxy - expect a few
seconds of latency.

- [ ] **Step 6: Push and commit**

```bash
cd theme
npx shopify theme push --store <your-dev-store>.myshopify.com --theme <theme-id> --allow-live
cd ..
git add theme/snippets/ask-about-product.liquid theme/assets/ask-about-product.js \
  theme/sections/main-product.liquid theme/templates/product.json
git commit -m "Add storefront 'Ask about this product' RAG box"
```

---

### Task 9 [USER ACTION, verification]: The ANC trap-question demo

- [ ] **Step 1:** Load the **Crusher 720** product page (no ANC) live on
  the storefront.
- [ ] **Step 2:** Ask: **"How do I turn on noise cancelling?"**
- [ ] **Step 3:** Confirm the "Without context" answer plausibly
  describes generic ANC steps (a hallucination - this model has no ANC
  controls to describe) while the "With context" answer correctly
  states this model doesn't have ANC, grounded in the retrieved chunk.
- [ ] **Step 4:** Repeat on the **Crusher 1080 ANC** or **Method 360
  ANC** page (both have real ANC) and confirm the "With context" answer
  correctly describes the real ANC controls (Quiet/Off/Aware modes,
  custom button or app).
- [ ] **Step 5:** Record the actual observed answers (both models) for
  the README - this exact before/after pair is the single strongest
  demo moment for this feature, worth documenting verbatim rather than
  just asserting it works.

---

### Task 10 [CODE, docs]: README update

**Files:**
- Modify: `README.md`
- Modify: `PLAN.md`

- [ ] **Step 1:** Add a "Phase 3 — RAG Product Q&A" section to
  `README.md`: what's built, the two new secrets required, the honest
  caveats from the design spec (original-not-scraped content, no vector
  DB, no rate limiting, real per-question cost/latency, Voyage AI as the
  embeddings provider since Anthropic has none), and the actual recorded
  ANC trap-question transcript from Task 9.
- [ ] **Step 2:** Add a "Phase 3" checklist to `PLAN.md`, checked off
  against what was actually built and verified.
- [ ] **Step 3:** Commit:
  ```bash
  git add README.md PLAN.md
  git commit -m "Document Phase 3 RAG product Q&A"
  git push
  ```

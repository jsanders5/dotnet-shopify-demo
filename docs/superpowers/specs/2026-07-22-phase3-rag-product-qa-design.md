# Phase 3 — RAG Product Q&A Design

**Status:** Approved, ready for implementation planning.

## Goal

Add a real "Ask about this product" feature to the four Skullcandy
headphone product pages, backed by retrieval-augmented generation (RAG)
over real product user-guide facts. The point isn't just "answer
questions" — it's to make the *value of RAG itself* visible, live, on
the storefront: every answer is shown two ways, with and without
retrieved context, so the difference is checkable, not asserted. This is
explicitly a bonus, beyond what the job posting asks for (see
`CLAUDE.md`), demonstrating AI/RAG proficiency using the same
infrastructure (App Proxy, Liquid, the .NET API) built in Phase 2 rather
than as a disconnected add-on.

## Prerequisites (done)

Four real Skullcandy headphone products exist in the dev store, created
via the Admin API with placeholder images (not real product photography)
and real prices:
- Skullcandy Crusher 720 — no ANC
- Skullcandy Crusher 1080 ANC — has ANC
- Skullcandy Method 360 ANC — has ANC
- Skullcandy Riff Wireless 2 — no ANC

## Content sourcing

Real factual content (button layouts, pairing steps, battery specs, ANC
behavior, troubleshooting) was pulled from Skullcandy's own public
product-help pages for each model. The text actually stored and served
by this feature is **written fresh, in original wording, based on those
facts** — not copied verbatim from Skullcandy's pages. Same honesty
pattern as the placeholder product images: real facts, original
material, clearly not official Skullcandy documentation. This gets
stated plainly in the README, not left implicit.

## Architecture

### Content model

Two new tables, deliberately separate from the inventory-tracking
`Product`/`InventoryLog` models — a product guide is static content tied
to a Shopify *product*, not stock level tied to a variant:

- `ProductGuide` — `Id`, `ShopifyProductId` (long), `Title`
- `ProductGuideChunk` — `Id`, `ProductGuideId` (FK), `Content` (text),
  `Embedding` (the chunk's Voyage AI embedding vector, stored as a
  JSON-serialized `float[]`)

Each product's guide is split into a handful of topic chunks (controls,
pairing/setup, battery/charging, ANC behavior where applicable,
troubleshooting) — roughly 20-30 chunks total across all four products.

### Seeding (one-off, not a CRUD feature)

A one-off script (same pattern as the earlier product-registration
scripts) that, once, for each chunk:
1. Calls Voyage AI's embeddings endpoint to get the chunk's vector.
2. Inserts the `ProductGuide`/`ProductGuideChunk` rows.

This runs once against the seeded content above. There is no admin UI
or CRUD endpoint for managing guide content — adding a fifth product's
guide later would mean writing and re-running the same kind of one-off
script, same as how the four headphone products themselves were created.

### The endpoint

`POST /api/products/{shopifyProductId}/ask` with body `{ "question": "..." }`:

1. Look up `ProductGuide` by `ShopifyProductId`. Not found → `404`.
2. Blank/missing question → `400`.
3. Embed the question via Voyage AI (**only** the question — the corpus
   was already embedded once at seed time; re-embedding static content
   on every request would be pure waste, see "Why only the question is
   embedded per-request" below).
4. Cosine-similarity search in C#, in memory, over that product's stored
   chunk embeddings (deserialize each `Embedding` back to `float[]`,
   compute similarity against the question's vector) — no vector DB;
   the corpus is small enough (~5-10 chunks per product) that a linear
   scan is genuinely the right amount of engineering, not a shortcut.
5. Take the top 3 chunks by similarity (fixed constant — each product
   only has 5-10 chunks total, so 3 reliably covers the relevant one(s)
   without diluting the context with irrelevant material).
6. Two Claude API calls: one with just the question, one with the
   question plus the retrieved chunks as context.
7. Return `{ question, answerWithoutContext, answerWithContext,
   retrievedChunks }` — the retrieved chunk text is returned too, so the
   "why" behind the grounded answer is checkable against real source
   text, not just asserted.

### Why only the question is embedded per-request

The guide corpus is static — it doesn't change between questions, so
its embeddings are computed once at seed time and reused for every
subsequent query. Only the new input each request actually introduces
(the question) needs a fresh embedding call. This is the standard RAG
pattern: embed the corpus once offline, embed the query per request —
not a simplification specific to this demo.

### Storefront integration

A new Liquid block/snippet (same `main-product` block pattern as the
low-stock badge) on each headphone's product page: a text input, a
submit button, and two response panels — "Without context" and "With
context" — plus the retrieved source passages shown under the grounded
answer. JS posts through the existing App Proxy
(`/apps/inventory/products/{id}/ask` — reuses the same proxy config as
the low-stock lookups; the `/apps/inventory` prefix is now a bit of a
misnomer for a Q&A feature, but it's just a routing prefix, not worth
renaming for that alone).

## The ANC trap-question demo

Two of the four products have active noise cancellation (Crusher 1080
ANC, Method 360 ANC); two don't (Crusher 720, Riff Wireless 2). Asking
**"how do I turn on noise cancelling?"** on a non-ANC model (e.g.
Crusher 720) is the single most concrete, checkable illustration of what
RAG buys here:
- **Without context:** Claude has no product-specific grounding and is
  likely to hallucinate plausible-sounding ANC steps that don't exist on
  this model — a confident wrong answer, not just a vague one.
- **With context:** grounded in the real chunk content (which correctly
  has no ANC section for this product), the answer correctly states
  this model doesn't have ANC.

This is documented as a specific, reproducible manual test in the
README — a stronger demo moment than a generic spec question, since the
failure mode (hallucinated capability, not just a weaker answer) is
concrete and verifiable.

## Error handling

- Unknown `shopifyProductId` → `404` (a real error here — unlike the
  low-stock lookups' "untracked item" case, "no guide for this product"
  isn't an expected/benign state).
- Blank question → `400`.
- Anthropic/Voyage failures (bad key, rate limit, timeout) → surfaced as
  a clear error response, not silently swallowed. No retry logic —
  disclosed simplification.
- **Honest cost/latency disclosure:** every question triggers one
  embedding call and two Claude calls — a few seconds of real latency
  and a small real dollar cost per question. No rate limiting or abuse
  protection on an endpoint reachable through the public tunnel while
  it's running. This is a demo, not a production-hardened feature.

## Testing

- The retrieval/cosine-similarity logic is pure C# with no external
  calls — unit tested directly (EF InMemory + fake embedding vectors),
  same rigor as the rest of the API.
- The actual Anthropic/Voyage API calls are **not** covered by
  automated tests — real keys, real network calls, real cost per test
  run. Verified manually instead, same disclosed pattern already used
  for the stored procedure and the Phase 2 webhook end-to-end test.
- The ANC trap-question scenario is the specific manual verification
  script recorded in the README (see above).

## Global Constraints

- Two new secrets required: `Anthropic:ApiKey` and `VoyageAi:ApiKey`,
  both via `dotnet user-secrets`, never committed, never pasted into
  chat — same handling as the Phase 2 webhook secret.
- Guide content is original writing based on real public facts, not
  scraped/copied Skullcandy text — stated plainly in the README.
- No admin UI/CRUD for guide content — seeded once via a one-off script.
- No chat history or multi-turn conversation — one question, one
  comparison response.
- No streaming responses — kept synchronous for simplicity.
- No vector DB — in-memory cosine similarity is the right amount of
  engineering at this corpus size (~20-30 chunks total), not a shortcut
  around real infrastructure.
- Never overstate in docs/comments/commits: this is a small, honestly-
  scoped RAG demo — disclose the corpus size, the lack of rate limiting,
  and the original (not scraped) content plainly if asked.

## Out of Scope

- Any product beyond the four headphones already created.
- A real embeddings/vector database (Pinecone, pgvector, etc.).
- Rate limiting, cost controls, or abuse protection on the endpoint.
- Multi-turn conversation / chat history.
- Managing guide content via CRUD/admin UI.

# CLAUDE.md — skullcandy-dotnet-shopify-demo

Context for Claude Code (or any Claude session) working in this repo.

## What this repo is

A scoped, 2-day demo project: **Shopify Inventory Sync & Storefront Alert**.
A small ASP.NET Core Web API (EF Core + SQL Server) that receives Shopify
inventory-update webhooks and persists them, paired with a Shopify storefront
theme customization (Liquid) that calls the API to show live stock status on
a product page.

This exists to honestly close real skill gaps ahead of applying to
Skullcandy's Full Stack Developer — .NET / Shopify posting (see
`job-posting.md`). The full build plan is in `PLAN.md`.

## Why this project exists — read this before writing any code or docs

Jacob (the user) has 13+ years of engineering experience, is a fast learner,
and does genuinely heavy AI-assisted development (Claude Code, production
Claude API apps). He does **not** have prior hands-on experience with:

- ASP.NET Core, Entity Framework, or IIS (has written C# before — a
  production desktop app at a past employer, Skullcandy — but not the .NET
  web stack)
- Shopify Liquid / theme development (zero prior experience — the single
  biggest gap versus the target job posting)
- Microsoft SQL Server specifically (prior DB experience is DynamoDB and
  Supabase/Postgres, not SQL Server)

**The whole point of this repo is to make these real**, not to fake them.
Two focused days won't produce senior-level .NET depth — it produces a real,
working, defensible demo he can discuss competently in an interview.

## The one hard rule

**Do not overstate what this project demonstrates, in code comments, README,
commit messages, or conversation.** If something is a toy/simplified version
of a real pattern, say so. If a piece (e.g. live IIS deployment) doesn't
actually get built, don't write docs implying it was. The credibility of
this whole exercise depends on it being defensible under direct interview
questioning — a hiring manager who worked at Skullcandy may ask detailed
follow-up questions.

This mirrors the standing rule from Jacob's resume repo
(`jacob-sanders-resume/CLAUDE.md`): don't claim skills or experience not
backed by the actual code/work.

## Known constraint: IIS

Local dev environment is macOS; IIS is Windows-only, so there's no local IIS
to deploy to. Two honest options (see `PLAN.md` for full reasoning):

1. Deploy to Azure App Service on a Windows plan (genuinely IIS under the
   hood), if Azure access is available.
2. Skip a live IIS deployment and say so plainly. Ship on Kestrel with
   `web.config` and the in-process hosting model configured correctly (so
   the app is demonstrably IIS-deployable) without claiming it was actually
   run under IIS.

Default to option 2 unless Azure access is confirmed available.

## Architecture (see PLAN.md for the day-by-day build order)

- **ASP.NET Core Web API** — Products/InventoryLog endpoints, DI, middleware
- **EF Core + SQL Server** (Azure SQL Edge via Colima, not a real SQL
  Server container or LocalDB — see "Decided" below) — Code-First models,
  a migration, and at least one raw stored procedure (e.g.
  `GetLowStockProducts` via `FromSqlRaw`) — this explicitly demonstrates
  stored-procedure experience, which the job posting calls out by name
- **Webhook receiver** — verifies the Shopify HMAC signature before
  persisting inventory updates to SQL Server
- **Shopify theme customization** — a Liquid section/snippet on a free
  Shopify Partner development store's product page (e.g. a "low stock"
  badge), calling the .NET API live via a Shopify App Proxy so the
  storefront call is same-origin
- **Bonus, Phase 3 — RAG product Q&A** — not in the original 2-day scope;
  added afterward to demonstrate AI-tool proficiency on top of the same
  App Proxy infrastructure. See `PLAN.md`'s Phase 3 section and
  `docs/superpowers/specs/2026-07-22-phase3-rag-product-qa-design.md`.

## Decided (was "Open questions")

All resolved during the build; see `PLAN.md`'s "What was decided" sections
for the full reasoning behind each.

- Shopify Partner account: signed up — a development store,
  `inventory-sync-demo`, with Shopify's own generated test-data catalog.
- SQL Server: neither Docker-SQL-Server nor LocalDB — Azure SQL Edge via
  Colima (Apple Silicon has no arm64 SQL Server image). Wire-compatible
  but a different product; see README's caveats.
- Azure access: not used — no live IIS deployment. Kestrel + a correctly
  configured `web.config`/in-process hosting model instead, per option 2
  above, disclosed plainly as never having actually run under IIS.
- Repo visibility: public —
  [`github.com/jsanders5/dotnet-shopify-demo`](https://github.com/jsanders5/dotnet-shopify-demo).

## Related files

- `job-posting.md` — verbatim Skullcandy Full Stack Developer posting this
  project targets
- `PLAN.md` — full 2-day build plan
- Resume this supports: `jacob_sanders_skullcandy_fullstack_resume.tex` in
  the `jacob-sanders-resume` repo (sibling directory). Once this project has
  a real, working state, add it there as a Projects entry — only describing
  what was actually built.

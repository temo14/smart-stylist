# Smart Stylist

An AI personal stylist that plans a wardrobe and buys only real deals, with delivered cost to Georgia.
This repository starts with **M0, the data test**: before any stylist features, we collect each tracked
product's price once a day from the shops you actually buy from, and find out which shops give clean data.
Real-discount verdicts need about 30 days of price history, so this runs first.

## What is here

| Part | What it does |
| --- | --- |
| `src/Modules/Catalog` | The Catalog module: shops, products, daily price snapshots, collectors, the data-quality report. |
| `src/SmartStylist.Worker` | Background host. Runs Hangfire and one daily price job per shop. Also a small CLI (`collect`, `report`). |
| `src/SmartStylist.Web` | Blazor web app. Today it shows the data-quality page and the Hangfire dashboard. |
| `tests/` | Parser tests with fixture pages and end-to-end collection tests against in-memory SQLite. |

It is a modular monolith: each business area becomes a module under `src/Modules` with its own
PostgreSQL schema (`catalog` here, later `wardrobe`, `orders`, `landedcost`), and both hosts load the modules they need.

## Quick start

You need the .NET 10 SDK and Docker.

```bash
docker compose up -d                                         # PostgreSQL 16 on localhost:5432
dotnet run --project src/SmartStylist.Worker -- install-browser   # once: headless Chromium for shops that block plain HTTP
dotnet run --project src/SmartStylist.Worker -- collect --limit 20   # quick test: 20 products per shop, then prints the report
```

That run takes roughly 10 to 20 minutes for all 15 shops, because the collector waits 1.5 s between pages.
It applies database migrations itself.

Other commands:

```bash
dotnet run --project src/SmartStylist.Worker -- collect levis uniqlo   # only some shops (full 300 products)
dotnet run --project src/SmartStylist.Worker -- report                 # print the report again
dotnet run --project src/SmartStylist.Worker                           # leave running: collects every shop daily at 05:00 UTC
dotnet run --project src/SmartStylist.Web                              # http://localhost:5227 (report) and /hangfire (jobs)
```

## How prices are collected

Each shop is tried with the cheapest source first, and the one that works is remembered for the next day:

1. **Shopify**: the public `/products.json` feed. Every product and size, with the "compare at" price. Cleanest data.
2. **SitemapJsonLd**: product URLs from `robots.txt` → sitemaps, then the schema.org JSON-LD (or Open Graph
   price tags) on each product page, over plain HTTP. Most big shops publish this for Google Shopping.
3. **BrowserJsonLd**: the same, but pages are opened in headless Chromium, for shops that block plain HTTP clients.

Up to 300 products per shop are priced each day (`Catalog:MaxProductsPerShop`). Products priced in the last
week are revisited first, so the same items build a continuous history. One price is stored per product size
per day; a second run on the same day overwrites it.

## The data-quality report

`collect` and `report` print a table, and the web app shows the same on its home page:

| Column | Meaning |
| --- | --- |
| Grade | **Clean**: 90%+ of pages gave a price and half or more of prices have a size. **Usable**: 60%+ of pages gave a price. **Poor**: less. **Failing**: no source worked on the last run. |
| Strategy | Which source worked. |
| Pages ok | Share of product pages on the last run that gave a price. |
| Sizes / Stock | Share of today's prices that come with a size / an in-stock flag. Per-size prices matter for fit. |
| On sale | Share of today's prices with a crossed-out "was" price. |
| Days | Days of history collected. Verdicts need about 30. |
| Moved | Products whose price changed across days, the signal we are after. |

Failing shops list the reason under the table, such as "HTTP 403" (blocked) or "no product URLs in the sitemap"
(the URL pattern needs tuning).

## The shops

Picked from your mailbox: shops you ordered from, then shops whose sale newsletters you get most.

| Key | Shop | Why |
| --- | --- | --- |
| `levis` | Levi's | Ordered from |
| `lululemon` | Lululemon | Ordered from |
| `adidas` | Adidas | Ordered from |
| `reebok` | Reebok | Ordered from |
| `underarmour` | Under Armour | Ordered from |
| `steepandcheap` | Steep & Cheap | Ordered from |
| `kohls` | Kohl's | Ordered from |
| `dressup` | Dressup.ge | Ordered from, Georgian shop (GEL) |
| `uniqlo` | Uniqlo | Frequent newsletters |
| `patagonia` | Patagonia | Frequent newsletters |
| `jcrew` | J.Crew | Frequent newsletters |
| `brooksbrothers` | Brooks Brothers | Frequent newsletters |
| `lacoste` | Lacoste | Frequent newsletters |
| `ssense` | SSENSE | Frequent newsletters |
| `mrporter` | Mr Porter | Frequent newsletters |

Backups if some fail: Arket, Massimo Dutti, Clarks, ECCO, Timberland, Huckberry, Thursday Boots.

The `ProductUrlPattern` values in `src/SmartStylist.Worker/appsettings.json` are educated guesses at each shop's
product URL shape (keep the US locale, men's section where obvious). They have not been checked against the
live sites yet. If a shop fails with "no product URLs in the sitemap", open its sitemap in a browser and fix the
pattern, or set `SitemapUrl` directly.

### Adding or tuning a shop

```json
{ "Key": "arket", "Name": "Arket", "BaseUrl": "https://www.arket.com/en-us", "Currency": "USD",
  "Strategy": "Auto", "ProductUrlPattern": "/en-us/product/", "SitemapUrl": null }
```

`Strategy` can be `Auto`, `Shopify`, `SitemapJsonLd` or `BrowserJsonLd`. Remove a shop from the list to
disable it; its history is kept.

## Development

```bash
dotnet test                       # no database or network needed
dotnet tool restore
dotnet ef migrations add <Name> --project src/Modules/Catalog/SmartStylist.Modules.Catalog \
  --startup-project src/SmartStylist.Worker --output-dir Persistence/Migrations
```

Money is `decimal(12,2)` with its currency on every row; nothing is converted yet.

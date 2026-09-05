# The Polite Scraper

## Target Classification

- **Site:** https://books.toscrape.com/
- **Why this site:** It is a fictional bookstore built specifically as a sandbox for practicing web scraping. The site states this explicitly: "A fictional bookstore that desperately wants to be scraped. It's a safe place for beginners learning web scraping and for developers validating their scraping technologies as well."
- **Scope:** The first 3 catalogue pages only. Each page lists 20 books, so this covers 60 books total.
- **Data collected:** Book title, price, star rating, and availability (in stock or not, and quantity available if in stock).
- **robots.txt result:** Requested once — returned 404. No robots file found. (Note: a missing robots.txt is not itself permission to scrape; permission here comes from the site's explicit self-description as a scraping sandbox, above.)

I will not reuse this code on another site without checking its rules and terms first.

## How to Run

```
git clone https://github.com/Youssef-Keashta/BE-05.git
cd BE-05
dotnet run
```

Clone the repo, navigate into it, and run it via PowerShell or Command Prompt.

## Lane & Setup

- **Language/runtime:** C# (.NET 8+)
- **Requirements:** [.NET 8 SDK](https://dotnet.microsoft.com/download) installed
- **Dependencies:** Restored automatically on `dotnet run` (uses AngleSharp via NuGet — no manual install needed)

## Record Schema

| Field | Description |
|---|---|
| `Title` | Title of the book |
| `ProductUrl` | Absolute URL of the book's page |
| `PriceText` | Raw text of the book's price |
| `PriceGBP` | Decimal value of the book's price |
| `AvailabilityText` | Raw text of the book's availability and stock |
| `RatingText` | Raw text of the book's star rating |
| `Description` | Raw description text (if available) |
| `SourcePage` | The catalogue page the book was discovered on |
| `FetchedAt` | ISO-8601 timestamp of when the page was fetched |

## Politeness Rules

This scraper follows professional scraping etiquette:

| Rule | Implementation |
|---|---|
| Identify yourself | Sends `User-Agent: FlyRankInternship_A9/1.0 (https://github.com/Youssef-Keashta/BE-05)` |
| Respect the server | 500ms delay between real (non-cached) requests |
| Timeout | HTTP requests give up after 10 seconds |
| Cache | Saves every response; development runs read from disk instead of hammering the site |
| Check status codes | Only HTTP 200 is treated as success |
| Retry transient failures | Timeouts and 5xx errors are retried once after 1 second |
| No retry on 4xx | 404 (not found) and 403 (forbidden) are not retried — asking again won't help |
| Survive failures | A single broken page is logged and skipped; the run continues |

## Sample Run Report

```json
{
  "StartTime": "2026-09-05T13:32:45.8951281Z",
  "DurationSeconds": 2.1006123,
  "PagesFetched": 63,
  "CacheHits": 63,
  "ValidRecords": 60,
  "InvalidRecords": 0,
  "FailedPages": 1,
  "Failures": [
    "Book page https://books.toscrape.com/catalogue/definitely-fake-book-99999/index.html: Fetch failed with status code: NotFound"
  ]
}
```

## Why No Browser Was Needed

The data on Books to Scrape is delivered in the initial HTML response from the server. There is no client-side rendering or JavaScript-driven content. Using a headless browser would add significant time and memory overhead without any benefit — a simple HTTP client with an HTML parser (AngleSharp) is the right tool for the job.

## Known Limitations

The main limitation encountered was in the website itself: some book descriptions on the site are cut off mid-sentence and then duplicated in full. This looks like a parsing bug at first glance, but it was verified against the live site's actual HTML — the duplication exists in the source itself, not in this scraper's extraction logic.

## Ethics Note

This scraper was built for educational purposes against a public sandbox that explicitly welcomes scraping. In production, always:

- Use an official API when one exists
- Never bypass login walls, paywalls, or IP blocks
- Collect only what you need — no more
- Respect `robots.txt` and `Retry-After` headers
- Identify yourself with a meaningful User-Agent
- Cache responses to avoid unnecessary load on the server

I will not reuse this code on another site without checking its rules and terms first.

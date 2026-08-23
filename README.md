# The Polite Scraper

## Target Classification

- **Site:** https://books.toscrape.com/
- **Why this site:** It is a fictional bookstore built specifically as a sandbox for practicing web scraping. The site states this explicitly: "A fictional bookstore that desperately wants to be scraped. It's a safe place for beginners learning web scraping and for developers validating their scraping technologies as well."
- **Scope:** The first 3 catalogue pages only. Each page lists 20 books, so this covers 60 books total.
- **Data collected:** Book title, price, star rating, and availability (in stock or not, and quantity available if in stock).
- **robots.txt result:** Requested once — returned 404. No robots file found. (Note: a missing robots.txt is not itself permission to scrape; permission here comes from the site's explicit self-description as a scraping sandbox, above.)

I will not reuse this code on another site without checking its rules and terms first.
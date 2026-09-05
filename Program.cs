using AngleSharp;
using AngleSharp.Dom;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace BE_05
{
    internal class Program
    {
        static readonly HttpClient client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        static async Task Main(string[] args)
        {
            var runStart = DateTime.UtcNow;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "FlyRankInternship_A9/1.0 (https://github.com/Youssef-Keashta/BE-05)");

            string cacheDir = "cache";
            Directory.CreateDirectory(cacheDir);

            string bookDir = @"cache\books";
            Directory.CreateDirectory(bookDir);

            string outputDir = "output";
            Directory.CreateDirectory(outputDir);

            int pagesFetched = 0;
            int cacheHits = 0;
            int failedPages = 0;
            var failureLog = new List<string>();

            const int maxPages = 3;
            var allBookLinks = new List<(string bookUrl, string sourcePage)>();
            string? currentUrl = "https://books.toscrape.com/catalogue/page-1.html";
            int pageCount = 0;

            // --- Catalogue pages ---
            while (currentUrl != null && pageCount < maxPages)
            {
                pageCount++;
                string cacheFile = Path.Combine(cacheDir, $"catalogue-page-{pageCount}.html");

                FetchResult result = await FetchWithCacheAsync(currentUrl, cacheFile);

                if (!result.Success)
                {
                    failedPages++;
                    failureLog.Add($"Catalogue page {pageCount} ({currentUrl}): {result.ErrorMessage}");
                    Console.WriteLine($"SKIPPED catalogue page {pageCount}: {result.ErrorMessage}");
                    currentUrl = null; // can't discover further pages without this one
                    continue;
                }

                pagesFetched++;
                if (result.WasCacheHit) cacheHits++;

                List<(string bookUrl, string sourcePage)> pageLinks = await ExtractBookLinksAsync(result.Html!, currentUrl);
                allBookLinks.AddRange(pageLinks);

                string? nextUrl = await ExtractNextPageUrlAsync(result.Html!, currentUrl);

                if (!result.WasCacheHit)
                {
                    await Task.Delay(500);
                }

                currentUrl = (pageCount < maxPages) ? nextUrl : null;
            }

            var uniqueUrls = allBookLinks
                .GroupBy(x => x.bookUrl)
                .Select(g => g.First())
                .ToList();

            //// Deliberately broken URL to prove Stage 5 works — remove before final submission if you only want it for testing
            //uniqueUrls.Add((
            //    "https://books.toscrape.com/catalogue/definitely-fake-book-99999/index.html",
            //    "test-injected"
            //));

            // --- Book detail pages ---
            var books = new List<RawBook>();

            for (int i = 0; i < uniqueUrls.Count; i++)
            {
                string bookUrl = uniqueUrls[i].bookUrl;
                string sourcePage = uniqueUrls[i].sourcePage;
                string cacheFile = Path.Combine(bookDir, $"book-{i + 1:000}.html");

                FetchResult result = await FetchWithCacheAsync(bookUrl, cacheFile);

                if (!result.Success)
                {
                    failedPages++;
                    failureLog.Add($"Book page {bookUrl}: {result.ErrorMessage}");
                    Console.WriteLine($"SKIPPED book page: {result.ErrorMessage}");

                    if (!result.WasCacheHit)
                    {
                        await Task.Delay(500);
                    }
                    continue;
                }

                pagesFetched++;
                if (result.WasCacheHit) cacheHits++;

                RawBook book = await ExtractBookData(result.Html!, bookUrl, sourcePage);
                books.Add(book);

                if (!result.WasCacheHit)
                {
                    await Task.Delay(500);
                }
            }

            Console.WriteLine($"catalogue_pages={pageCount}");
            Console.WriteLine($"discovered={allBookLinks.Count}");
            Console.WriteLine($"unique_urls={uniqueUrls.Count}");
            Console.WriteLine($"detail_pages_fetched={books.Count}");
            Console.WriteLine($"failed_pages={failedPages}");
            Console.WriteLine();

            List<Book> normalizedBooks = books.Select(NormalizeBook).ToList();

            var duplicateUrls = normalizedBooks
                .GroupBy(b => b.ProductUrl)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet();

            var validBooks = new List<Book>();
            var invalidBooks = new List<(Book book, List<string> reasons)>();

            foreach (var book in normalizedBooks)
            {
                var reasons = new List<string>();

                var validationResults = ValidateBook(book);
                reasons.AddRange(validationResults.Select(r => r.ErrorMessage ?? "Unknown validation error"));

                if (duplicateUrls.Contains(book.ProductUrl))
                {
                    reasons.Add($"Duplicate ProductUrl: {book.ProductUrl}");
                }

                if (reasons.Count == 0)
                {
                    validBooks.Add(book);
                }
                else
                {
                    invalidBooks.Add((book, reasons));
                }
            }

            Console.WriteLine($"valid_books={validBooks.Count}");
            Console.WriteLine($"invalid_books={invalidBooks.Count}");

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

            string booksJsonPath = Path.Combine(outputDir, "books.json");
            await File.WriteAllTextAsync(booksJsonPath, JsonSerializer.Serialize(validBooks, jsonOptions));

            var errorRecords = invalidBooks.Select(x => new { Book = x.book, Reasons = x.reasons }).ToList();
            string errorsJsonPath = Path.Combine(outputDir, "errors.json");
            await File.WriteAllTextAsync(errorsJsonPath, JsonSerializer.Serialize(errorRecords, jsonOptions));

            var runEnd = DateTime.UtcNow;
            var report = new RunReport
            {
                StartTime = runStart,
                DurationSeconds = (runEnd - runStart).TotalSeconds,
                PagesFetched = pagesFetched,
                CacheHits = cacheHits,
                ValidRecords = validBooks.Count,
                InvalidRecords = invalidBooks.Count,
                FailedPages = failedPages,
                Failures = failureLog
            };

            string reportPath = Path.Combine(outputDir, "run-report.json");
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, jsonOptions));

            Console.WriteLine();
            Console.WriteLine($"Wrote {validBooks.Count} valid records to {booksJsonPath}");
            Console.WriteLine($"Wrote {invalidBooks.Count} invalid records to {errorsJsonPath}");
            Console.WriteLine($"Wrote run report to {reportPath}");
        }

        static async Task<FetchResult> FetchWithCacheAsync(string url, string cacheFile)
        {
            if (File.Exists(cacheFile))
            {
                string cachedHtml = await File.ReadAllTextAsync(cacheFile);
                Console.WriteLine($"CACHE HIT\nResponse size: {cachedHtml.Length} bytes");
                return new FetchResult { Success = true, Html = cachedHtml, WasCacheHit = true };
            }

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                HttpResponseMessage response;
                try
                {
                    response = await client.GetAsync(url);
                }
                catch (TaskCanceledException)
                {
                    if (attempt < 2)
                    {
                        Console.WriteLine($"Timeout on attempt {attempt} for {url}, retrying...");
                        await Task.Delay(1000);
                        continue;
                    }
                    return new FetchResult { Success = false, ErrorMessage = "Request timed out after retry." };
                }
                catch (HttpRequestException e)
                {
                    return new FetchResult { Success = false, ErrorMessage = $"Network error: {e.Message}" };
                }

                if (response.IsSuccessStatusCode)
                {
                    string html = await response.Content.ReadAsStringAsync();
                    await File.WriteAllTextAsync(cacheFile, html);
                    Console.WriteLine($"FETCH\nResponse size: {html.Length} bytes");
                    return new FetchResult { Success = true, Html = html, WasCacheHit = false };
                }

                int statusCode = (int)response.StatusCode;
                bool isServerError = statusCode >= 500 && statusCode < 600;

                if (isServerError && attempt < 2)
                {
                    Console.WriteLine($"Server error {statusCode} on attempt {attempt} for {url}, retrying...");
                    await Task.Delay(1000);
                    continue;
                }

                return new FetchResult { Success = false, ErrorMessage = $"Fetch failed with status code: {response.StatusCode}" };
            }

            return new FetchResult { Success = false, ErrorMessage = "Unknown fetch failure." };
        }

        static async Task<List<(string bookUrl, string sourcePage)>> ExtractBookLinksAsync(string html, string url)
        {
            Uri baseUri = new Uri(url);
            var context = BrowsingContext.New(Configuration.Default);
            IDocument document = await context.OpenAsync(req => req.Content(html));
            var linkElements = document.QuerySelectorAll("article.product_pod h3 a");

            var hrefs = new List<(string, string)>();
            foreach (var element in linkElements)
            {
                string? href = element.GetAttribute("href");
                if (href != null)
                {
                    Uri absoluteUri = new Uri(baseUri, href);
                    hrefs.Add((absoluteUri.ToString(), url));
                }
            }

            return hrefs;
        }

        static async Task<string?> ExtractNextPageUrlAsync(string html, string baseUrl)
        {
            Uri baseUri = new Uri(baseUrl);
            var context = BrowsingContext.New(Configuration.Default);
            IDocument document = await context.OpenAsync(req => req.Content(html));
            var linkElement = document.QuerySelector("li.next a");
            string? href = linkElement?.GetAttribute("href");
            if (href != null)
            {
                Uri absoluteUri = new Uri(baseUri, href);
                return absoluteUri.ToString();
            }
            return null;
        }

        static async Task<RawBook> ExtractBookData(string html, string bookUrl, string pageUrl)
        {
            var context = BrowsingContext.New(Configuration.Default);
            IDocument document = await context.OpenAsync(req => req.Content(html));

            var title = document.QuerySelector("h1");
            var price = document.QuerySelector(".price_color");
            var availability = document.QuerySelector(".availability");
            var rating = document.QuerySelector("p.star-rating")?.ClassList;
            var description = document.QuerySelector("#product_description");

            return new RawBook
            {
                Title = title?.TextContent,
                PriceText = price?.TextContent,
                AvailabilityText = availability?.TextContent,
                RatingText = rating?.FirstOrDefault(c => c != "star-rating"),
                Description = description?.NextElementSibling?.TextContent,
                SourcePage = pageUrl,
                ProductUrl = bookUrl,
                FetchedAt = DateTime.UtcNow.ToString("o")
            };
        }

        static Book NormalizeBook(RawBook raw)
        {
            decimal price;
            string? priceText = raw?.PriceText;
            if (!string.IsNullOrEmpty(priceText))
            {
                string normalizedPrice = priceText.TrimStart().TrimStart('£').Trim();
                if (!decimal.TryParse(normalizedPrice, out price))
                {
                    price = -1m;
                }
            }
            else price = -1m;

            return new Book
            {
                Title = raw?.Title,
                PriceText = raw?.PriceText,
                PriceGBP = price,
                AvailabilityText = raw?.AvailabilityText,
                RatingText = raw?.RatingText,
                Description = raw?.Description,
                SourcePage = raw?.SourcePage,
                ProductUrl = raw?.ProductUrl,
                FetchedAt = raw?.FetchedAt
            };
        }

        static List<ValidationResult> ValidateBook(Book book)
        {
            var context = new ValidationContext(book);
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(book, context, results, validateAllProperties: true);
            return results;
        }
    }

    internal class FetchResult
    {
        public bool Success { get; set; }
        public string? Html { get; set; }
        public bool WasCacheHit { get; set; }
        public string? ErrorMessage { get; set; }
    }

    internal class RunReport
    {
        public DateTime StartTime { get; set; }
        public double DurationSeconds { get; set; }
        public int PagesFetched { get; set; }
        public int CacheHits { get; set; }
        public int ValidRecords { get; set; }
        public int InvalidRecords { get; set; }
        public int FailedPages { get; set; }
        public List<string> Failures { get; set; } = new();
    }
}
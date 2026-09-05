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
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "FlyRankInternship_A9/1.0 (https://github.com/Youssef-Keashta/BE-05)");

            string cacheDir = "cache";
            Directory.CreateDirectory(cacheDir);

            string bookDir = @"cache\books";
            Directory.CreateDirectory(bookDir);

            string outputDir = "output";
            Directory.CreateDirectory(outputDir);

            const int maxPages = 3;
            var allBookLinks = new List<(string bookUrl, string sourcePage)>();
            string? currentUrl = "https://books.toscrape.com/catalogue/page-1.html";
            int pageCount = 0;

            try
            {
                while (currentUrl != null && pageCount < maxPages)
                {
                    pageCount++;
                    string cacheFile = Path.Combine(cacheDir, $"catalogue-page-{pageCount}.html");

                    var (html, wasCacheHit) = await FetchWithCacheAsync(currentUrl, cacheFile);

                    List<(string bookUrl, string sourcePage)> pageLinks = await ExtractBookLinksAsync(html, currentUrl);
                    allBookLinks.AddRange(pageLinks);

                    string? nextUrl = await ExtractNextPageUrlAsync(html, currentUrl);

                    if (!wasCacheHit)
                    {
                        await Task.Delay(500);
                    }

                    currentUrl = (pageCount < maxPages) ? nextUrl : null;
                }

                var uniqueUrls = allBookLinks
                    .GroupBy(x => x.bookUrl)
                    .Select(g => g.First())
                    .ToList();

                var books = new List<RawBook>();

                for (int i = 0; i < uniqueUrls.Count; i++)
                {
                    string bookUrl = uniqueUrls[i].bookUrl;
                    string sourcePage = uniqueUrls[i].sourcePage;
                    string cacheFile = Path.Combine(bookDir, $"book-{i + 1:000}.html");

                    var (html, wasCacheHit) = await FetchWithCacheAsync(bookUrl, cacheFile);

                    RawBook book = await ExtractBookData(html, bookUrl, sourcePage);
                    books.Add(book);

                    if (!wasCacheHit)
                    {
                        await Task.Delay(500);
                    }
                }

                Console.WriteLine($"catalogue_pages={pageCount}");
                Console.WriteLine($"discovered={allBookLinks.Count}");
                Console.WriteLine($"unique_urls={uniqueUrls.Count}");
                Console.WriteLine($"detail_pages={books.Count}");
                Console.WriteLine();

                // Normalize
                List<Book> normalizedBooks = books.Select(NormalizeBook).ToList();

                // Cross-record uniqueness check
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

                // Serialize valid records
                var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

                string booksJsonPath = Path.Combine(outputDir, "books.json");
                string booksJson = JsonSerializer.Serialize(validBooks, jsonOptions);
                await File.WriteAllTextAsync(booksJsonPath, booksJson);

                // Serialize invalid records + reasons
                var errorRecords = invalidBooks.Select(x => new
                {
                    Book = x.book,
                    Reasons = x.reasons
                }).ToList();

                string errorsJsonPath = Path.Combine(outputDir, "errors.json");
                string errorsJson = JsonSerializer.Serialize(errorRecords, jsonOptions);
                await File.WriteAllTextAsync(errorsJsonPath, errorsJson);

                Console.WriteLine();
                Console.WriteLine($"Wrote {validBooks.Count} valid records to {booksJsonPath}");
                Console.WriteLine($"Wrote {invalidBooks.Count} invalid records to {errorsJsonPath}");
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine($"Fetch failed: {e.Message}");
            }
        }

        static async Task<(string html, bool wasCacheHit)> FetchWithCacheAsync(string url, string cacheFile)
        {
            if (File.Exists(cacheFile))
            {
                string cachedHtml = await File.ReadAllTextAsync(cacheFile);
                Console.WriteLine($"CACHE HIT\nResponse size: {cachedHtml.Length} bytes");
                return (cachedHtml, true);
            }

            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(url);
            }
            catch (TaskCanceledException)
            {
                throw new HttpRequestException("Request timed out.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Fetch failed with status code: {response.StatusCode}");
            }

            string html = await response.Content.ReadAsStringAsync();
            await File.WriteAllTextAsync(cacheFile, html);

            Console.WriteLine($"FETCH\nResponse size: {html.Length} bytes");
            return (html, false);
        }

        static async Task<List<(string bookUrl, string sourcePage)>> ExtractBookLinksAsync(string html, string url)
        {
            Uri baseUri = new Uri(url);
            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
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
            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
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
            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
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
}
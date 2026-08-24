using AngleSharp;
using AngleSharp.Dom;

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

                var books = new List<Book>();

                for (int i = 0; i < uniqueUrls.Count; i++)
                {
                    string bookUrl = uniqueUrls[i].bookUrl;
                    string sourcePage = uniqueUrls[i].sourcePage;
                    string cacheFile = Path.Combine(bookDir, $"book-{i + 1:000}.html");

                    var (html, wasCacheHit) = await FetchWithCacheAsync(bookUrl, cacheFile);

                    Book book = await ExtractBookData(html, bookUrl, sourcePage);
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

                Console.WriteLine("Sample record:");
                Console.WriteLine($"  Title: {books[0].Title}");
                Console.WriteLine($"  ProductUrl: {books[0].ProductUrl}");
                Console.WriteLine($"  PriceText: {books[0].PriceText}");
                Console.WriteLine($"  AvailabilityText: {books[0].AvailabilityText}");
                Console.WriteLine($"  RatingText: {books[0].RatingText}");
                Console.WriteLine($"  Description: {books[0].Description ?? "null"}");
                Console.WriteLine($"  SourcePage: {books[0].SourcePage}");
                Console.WriteLine($"  FetchedAt: {books[0].FetchedAt}");

                var noDescriptionExample = books.FirstOrDefault(b => b.Description == null);
                Console.WriteLine();
                Console.WriteLine(noDescriptionExample != null
                    ? $"Found a book with null description: {noDescriptionExample.Title}"
                    : "No book with null description found in this batch.");
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

        static async Task<Book> ExtractBookData(string html, string bookUrl, string pageUrl)
        {
            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            IDocument document = await context.OpenAsync(req => req.Content(html));

            var title = document.QuerySelector("h1");
            var price = document.QuerySelector(".price_color");
            var availability = document.QuerySelector(".availability");
            var rating = document.QuerySelector("p.star-rating")?.ClassList;
            var description = document.QuerySelector("#product_description");

            return new Book
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
    }
}
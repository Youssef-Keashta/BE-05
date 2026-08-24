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

            const int maxPages = 3;
            var allBookLinks = new List<string>();
            string? currentUrl = "https://books.toscrape.com/catalogue/page-1.html";
            int pageCount = 0;

            try
            {
                while (currentUrl != null && pageCount < maxPages)
                {
                    pageCount++;
                    string cacheFile = Path.Combine(cacheDir, $"catalogue-page-{pageCount}.html");

                    var (html, wasCacheHit) = await FetchWithCacheAsync(currentUrl, cacheFile);

                    List<string> pageLinks = await ExtractBookLinksAsync(html, currentUrl);
                    allBookLinks.AddRange(pageLinks);

                    string? nextUrl = await ExtractNextPageUrlAsync(html, currentUrl);

                    if (!wasCacheHit)
                    {
                        await Task.Delay(500);
                    }

                    currentUrl = (pageCount < maxPages) ? nextUrl : null;
                }

                var uniqueUrls = allBookLinks.Distinct().ToList();

                Console.WriteLine($"catalogue_pages={pageCount}");
                Console.WriteLine($"discovered={allBookLinks.Count}");
                Console.WriteLine($"unique_urls={uniqueUrls.Count}");
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

        static async Task<List<string>> ExtractBookLinksAsync(string html, string url)
        {
            Uri baseUri = new Uri(url);
            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            IDocument document = await context.OpenAsync(req => req.Content(html));
            var linkElements = document.QuerySelectorAll("article.product_pod h3 a");

            var hrefs = new List<string>();
            foreach (var element in linkElements)
            {
                string? href = element.GetAttribute("href");
                if (href != null)
                {
                    Uri absoluteUri = new Uri(baseUri, href);
                    hrefs.Add(absoluteUri.ToString());
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
    }
}
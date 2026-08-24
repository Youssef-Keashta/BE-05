namespace BE_05
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            string url = "https://books.toscrape.com/catalogue/page-1.html";
            string projectDirectory = Directory.GetParent(Environment.CurrentDirectory).Parent.Parent.FullName;
            string cacheDir = Path.Combine(projectDirectory, "cache");
            string cacheFile = Path.Combine(cacheDir, "catalogue-page-1.html");

            if (File.Exists(cacheFile))
            {
                string cachedHtml = await File.ReadAllTextAsync(cacheFile);
                Console.WriteLine("CACHE HIT");
                Console.WriteLine($"Response size: {cachedHtml.Length} bytes");
            }
            else
            {
                using HttpClient client = new HttpClient();

                client.Timeout = TimeSpan.FromSeconds(10);

                client.DefaultRequestHeaders.UserAgent.ParseAdd("FlyRankInternship_A9/1.0 (https://github.com/Youssef-Keashta/BE-05)");

                try
                {
                    HttpResponseMessage response = await client.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        string html = await response.Content.ReadAsStringAsync();

                        await File.WriteAllTextAsync(cacheFile, html);

                        Console.WriteLine("FETCH");
                        Console.WriteLine($"Response size: {html.Length} bytes");
                    }
                    else
                    {
                        Console.WriteLine($"Fetch failed with status code: {response.StatusCode}");
                    }
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("Fetch failed: Request timed out.");
                }
            }
        }
    }
}

using System.Globalization;

namespace ScrapyNet.Sandbox;

public sealed record SandboxQuote(int Id, string Text, string Author, string AuthorSlug, IReadOnlyList<string> Tags);

public sealed record SandboxAuthor(string Slug, string Name, string Born, string Location, string Bio);

public sealed record SandboxBook(int Id, string Title, string Slug, string Category, decimal Price, int Rating, int Stock, string Upc, string Description)
{
    public string Url => $"/books/catalogue/{Slug}_{Id}/index.html";
    public string ImageUrl => $"/books/media/{Id}.png";
    public string RatingWord => Rating switch { 1 => "One", 2 => "Two", 3 => "Three", 4 => "Four", _ => "Five" };
}

public sealed record SandboxArticle(string Slug, string Title, string Author, DateTimeOffset Published, string Category, IReadOnlyList<string> Paragraphs);

/// <summary>The sandbox's fixed data set: quotes, authors, books and news articles. Deterministic, so tests can assert exact counts.</summary>
public static class SandboxData
{
    public const int QuotesPerPage = 10;
    public const int BooksPerPage = 20;

    public static readonly IReadOnlyList<SandboxAuthor> Authors =
    [
        new("albert-einstein", "Albert Einstein", "March 14, 1879", "in Ulm, Germany", "Theoretical physicist who developed the theory of relativity."),
        new("jane-austen", "Jane Austen", "December 16, 1775", "in Steventon Rectory, Hampshire, England", "English novelist known for her six major novels."),
        new("marie-curie", "Marie Curie", "November 07, 1867", "in Warsaw, Poland", "Physicist and chemist who conducted pioneering research on radioactivity."),
        new("mark-twain", "Mark Twain", "November 30, 1835", "in Florida, Missouri, The United States", "American writer, humorist and lecturer."),
        new("ki-hajar-dewantara", "Ki Hajar Dewantara", "May 02, 1889", "in Yogyakarta, Indonesia", "Pioneer of education in Indonesia and founder of Taman Siswa."),
        new("maya-angelou", "Maya Angelou", "April 04, 1928", "in St. Louis, Missouri, The United States", "American poet, memoirist and civil rights activist."),
        new("ada-lovelace", "Ada Lovelace", "December 10, 1815", "in London, England", "Mathematician regarded as the first computer programmer."),
        new("rumi", "Rumi", "September 30, 1207", "in Balkh, Khorasan", "Thirteenth-century Persian poet and Sufi mystic."),
    ];

    private static readonly string[] QuoteTexts =
    [
        "The world as we have created it is a process of our thinking. It cannot be changed without changing our thinking.",
        "It is our choices that show what we truly are, far more than our abilities.",
        "Nothing in life is to be feared, it is only to be understood.",
        "The secret of getting ahead is getting started.",
        "Setiap orang menjadi guru, setiap rumah menjadi sekolah.",
        "You will face many defeats in life, but never let yourself be defeated.",
        "The more I study, the more insatiable do I feel my genius for it to be.",
        "What you seek is seeking you.",
        "Imagination is more important than knowledge.",
        "There is no charm equal to tenderness of heart.",
        "Be less curious about people and more curious about ideas.",
        "Twenty years from now you will be more disappointed by the things that you didn't do than by the ones you did do.",
        "Ing ngarsa sung tuladha, ing madya mangun karsa, tut wuri handayani.",
        "If you don't like something, change it. If you can't change it, change your attitude.",
        "That brain of mine is something more than merely mortal; as time will show.",
        "Let yourself be silently drawn by the strange pull of what you really love.",
        "Life is like riding a bicycle. To keep your balance, you must keep moving.",
        "I declare after all there is no enjoyment like reading!",
        "One never notices what has been done; one can only see what remains to be done.",
        "Kindness is a language which the deaf can hear and the blind can see.",
    ];

    private static readonly string[] TagPool =
        ["life", "inspirational", "science", "humor", "education", "love", "books", "change", "wisdom", "courage", "truth", "learning"];

    public static readonly IReadOnlyList<SandboxQuote> Quotes = BuildQuotes();

    public static readonly IReadOnlyList<string> Categories = ["Travel", "Mystery", "Science", "Poetry", "History", "Fiction"];

    public static readonly IReadOnlyList<SandboxBook> Books = BuildBooks();

    public static readonly IReadOnlyList<SandboxArticle> Articles = BuildArticles();

    public static int QuotePages => (Quotes.Count + QuotesPerPage - 1) / QuotesPerPage;

    public static int BookPages => (Books.Count + BooksPerPage - 1) / BooksPerPage;

    private static List<SandboxQuote> BuildQuotes()
    {
        var list = new List<SandboxQuote>();
        var rnd = new Random(42);
        for (var i = 0; i < 50; i++)
        {
            var author = Authors[i % Authors.Count];
            var text = QuoteTexts[i % QuoteTexts.Length];
            if (i >= QuoteTexts.Length) text = text.TrimEnd('.', '!') + $" (vol. {i / QuoteTexts.Length + 1})";
            var tags = TagPool.OrderBy(_ => rnd.Next()).Take(1 + rnd.Next(3)).OrderBy(t => t, StringComparer.Ordinal).ToList();
            list.Add(new SandboxQuote(i + 1, text, author.Name, author.Slug, tags));
        }
        return list;
    }

    private static List<SandboxBook> BuildBooks()
    {
        string[] adjectives = ["Silent", "Golden", "Hidden", "Lost", "Crimson", "Quiet", "Endless", "Northern", "Paper", "Iron", "Last", "Wandering"];
        string[] nouns = ["Harbor", "Garden", "Algorithm", "Island", "Library", "River", "Engine", "Lantern", "Compass", "Orchard", "Voyage", "Archive"];
        var rnd = new Random(7);
        var list = new List<SandboxBook>();
        for (var i = 1; i <= 60; i++)
        {
            // Adjective and noun cycle every 12 books; a volume numeral keeps titles unique.
            var title = $"The {adjectives[i % adjectives.Length]} {nouns[(i * 5) % nouns.Length]}" + (i > 12 ? $" {RomanNumeral((i - 1) / 12 + 1)}" : "");
            var slug = title.ToLowerInvariant().Replace(' ', '-');
            var price = Math.Round(10m + (decimal)rnd.NextDouble() * 50m, 2);
            var rating = 1 + rnd.Next(5);
            var stock = rnd.Next(0, 23);
            var category = Categories[i % Categories.Count];
            var upc = Convert.ToHexStringLower(BitConverter.GetBytes((long)i * 2654435761L));
            var description = $"{title} is a {category.ToLowerInvariant()} title about {nouns[(i * 3) % nouns.Length].ToLowerInvariant()}s, " +
                              $"memory and the quiet craft of paying attention. Book {i} of the Scrapy.Net sandbox catalogue.";
            list.Add(new SandboxBook(i, title, slug, category, price, rating, stock, upc, description));
        }
        return list;
    }

    // Properties, not fields: static fields initialize in declaration order, and Articles (above) needs these.
    private static string[] TopicLeads =>
    [
        "Search engines and research crawlers both follow links, but they see a page as markup, headers and status codes rather than pixels.",
        "Etika scraping dimulai dari robots.txt, identitas user agent yang jelas, dan jeda antar permintaan agar server tidak terbebani.",
        "A vector database indexes embeddings so that the nearest neighbours of a query can be found in milliseconds among millions of chunks.",
        "Retrieval-augmented generation grounds a language model in documents it retrieves at question time instead of relying on its memory.",
        "Incremental crawling revisits only what changed, using ETags, Last-Modified headers and content hashes to skip identical pages.",
        "JSON-LD blocks describe products, articles and events with schema.org vocabulary that is far more stable than the visual layout.",
        "Proxy rotation spreads requests across many exit addresses and benches any proxy that starts receiving bans or captchas.",
        "Newsrooms syndicate stories widely, so the same article often appears on dozens of sites with only a line or two changed.",
        "Crawl statistics such as responses per minute, error rates and item counts are the vital signs of a scraping operation.",
        "Public documentation sites make excellent knowledge bases because they are well structured, versioned and meant to be read.",
        "Single page applications render content with JavaScript, so a headless browser is needed to see what a human visitor sees.",
        "Cron expressions describe schedules compactly: five fields for minute, hour, day of month, month and day of week.",
    ];

    private static string[] SentenceBank =>
    [
        "Seed URLs are the first requests a spider sends.",
        "Selectors turn raw markup into structured fields.",
        "Duplicate filters remember request fingerprints.",
        "A slow server deserves a longer download delay.",
        "Pagination links lead the crawler through listings.",
        "Item pipelines validate and clean every record.",
        "Feed exporters write results as JSON, CSV or XML.",
        "Middleware can rewrite requests before they leave.",
        "Retries recover from timeouts and gateway errors.",
        "Cookies keep a logged-in session alive between pages.",
        "Sitemaps list the pages a site wants to be found.",
        "Embeddings map sentences to points in a vector space.",
        "Chunk overlap preserves context across boundaries.",
        "Cosine similarity measures the angle between vectors.",
        "Summaries help readers triage long documents quickly.",
        "Categorization routes documents to the right team.",
        "Stale content should be refreshed on a schedule.",
        "Alerts fire when item counts suddenly drop.",
        "Rate limits protect small websites from overload.",
        "User agents identify the crawler to site owners.",
        "Structured data survives redesigns better than CSS classes.",
        "Headless browsers execute scripts before extraction.",
        "Job history shows how each run compares with the last.",
        "Secrets such as API keys belong in an encrypted store.",
        "Proxies with too many failures are temporarily benched.",
        "A knowledge base answers questions with cited sources.",
        "Change detection avoids reprocessing identical pages.",
        "Depth limits keep a crawl from wandering too far.",
        "Priority queues let important pages jump the line.",
        "Statistics reveal bottlenecks in the download pipeline.",
        "Language models draft answers from retrieved passages.",
        "Canonical links point to the preferred URL of a page.",
        "Images and files are downloaded by media pipelines.",
        "Validation drops records that miss required fields.",
        "Normalization unifies quotes, dashes and whitespace.",
        "Monitoring dashboards chart request rates over time.",
        "Webhooks notify other systems when a job finishes.",
        "Robots rules can allow one page inside a blocked folder.",
        "Throttling adapts to latency measured on every response.",
        "Incremental runs finish faster and cost less bandwidth.",
    ];

    private static string RomanNumeral(int n) => n switch { 2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => "I" };

    private static List<SandboxArticle> BuildArticles()
    {
        string[] topics =
        [
            "How web crawlers see the internet",
            "Menjaga etika scraping: robots.txt dan rate limit",
            "Vector databases explained for busy engineers",
            "The rise of retrieval-augmented generation",
            "Why incremental crawling saves money",
            "Structured data with JSON-LD in practice",
            "Proxy rotation without getting banned",
            "Deduplicating near-identical news stories",
            "Measuring crawl health with statistics",
            "Building a knowledge base from public documentation",
            "Crawling JavaScript-heavy single page apps",
            "Scheduling crawls with cron expressions",
        ];
        string[] categories = ["engineering", "ethics", "ai", "ai", "engineering", "data", "engineering", "ai", "operations", "ai", "engineering", "operations"];
        var list = new List<SandboxArticle>();
        var start = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < topics.Length; i++)
        {
            var title = topics[i];
            var slug = new string(title.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
            while (slug.Contains("--")) slug = slug.Replace("--", "-");
            // Each article draws its own sentences from the bank, so articles differ enough for
            // near-duplicate detection to tell them apart (only the syndicated copy below is a near-duplicate).
            var rnd = new Random(1000 + i);
            var picks = SentenceBank.OrderBy(_ => rnd.Next()).Take(12).ToList();
            var paragraphs = new List<string>
            {
                $"{title}. {TopicLeads[i]}",
                string.Join(" ", picks.Take(4)),
                string.Join(" ", picks.Skip(4).Take(4)),
                $"When the goal is {categories[i]} work, {string.Join(" ", picks.Skip(8).Take(2)).TrimEnd('.')}.",
                string.Join(" ", picks.Skip(10)),
            };
            list.Add(new SandboxArticle(slug, title, Authors[i % Authors.Count].Name, start.AddDays(i * 9), categories[i], paragraphs));
        }
        // A near-duplicate of the first article, for duplicate-content detection demos.
        var first = list[0];
        list.Add(first with
        {
            Slug = first.Slug + "-syndicated",
            Title = first.Title + " (syndicated)",
            Published = first.Published.AddDays(1),
            Paragraphs = [.. first.Paragraphs, "Republished with permission from the Scrapy.Net sandbox newsroom."],
        });
        return list;
    }

    public static string Money(decimal value) => "£" + value.ToString("0.00", CultureInfo.InvariantCulture);
}

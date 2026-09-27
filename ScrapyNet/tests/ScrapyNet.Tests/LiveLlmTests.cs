using ScrapyNet.AI;
using ScrapyNet.Sandbox;
using Xunit;

namespace ScrapyNet.Tests;

/// <summary>
/// Integration tests against real language models. They run only when credentials are provided through
/// environment variables and are skipped otherwise (CI has no secrets):
/// <list type="bullet">
/// <item>Azure OpenAI: <c>SCRAPYNET_AZURE_OPENAI_ENDPOINT</c>, <c>SCRAPYNET_AZURE_OPENAI_KEY</c>, <c>SCRAPYNET_AZURE_OPENAI_DEPLOYMENT</c></item>
/// <item>DeepSeek (OpenAI-compatible): <c>SCRAPYNET_DEEPSEEK_KEY</c>, optional <c>SCRAPYNET_DEEPSEEK_MODEL</c></item>
/// </list>
/// </summary>
public class LiveLlmTests(SandboxFixture sandbox)
{
    public static IEnumerable<TheoryDataRow<string>> Providers() => [new("azure"), new("deepseek")];

    private static ILanguageModel? Create(string provider)
    {
        switch (provider)
        {
            case "azure":
                var endpoint = Environment.GetEnvironmentVariable("SCRAPYNET_AZURE_OPENAI_ENDPOINT");
                var key = Environment.GetEnvironmentVariable("SCRAPYNET_AZURE_OPENAI_KEY");
                var deployment = Environment.GetEnvironmentVariable("SCRAPYNET_AZURE_OPENAI_DEPLOYMENT") ?? "gpt-5-mini";
                return endpoint is null || key is null ? null : OpenAiChatModel.ForAzure(endpoint, key, deployment);
            default:
                var dsKey = Environment.GetEnvironmentVariable("SCRAPYNET_DEEPSEEK_KEY");
                var model = Environment.GetEnvironmentVariable("SCRAPYNET_DEEPSEEK_MODEL") ?? "deepseek-chat";
                return dsKey is null ? null : new OpenAiChatModel(model, dsKey, "https://api.deepseek.com/v1/");
        }
    }

    private static ILanguageModel Require(string provider)
    {
        var model = Create(provider);
        if (model is null) Assert.Skip($"No credentials for {provider} (see LiveLlmTests docs).");
        return model;
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Llm_summarizes_and_categorizes_crawled_articles(string provider)
    {
        var model = Require(provider);
        var article = SandboxData.Articles.First(a => a.Title.Contains("Proxy rotation"));
        var text = string.Join("\n\n", article.Paragraphs);

        var summary = await new LlmSummarizer(model) { Language = "English" }.SummarizeAsync(text, 2, TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(summary));
        Assert.Contains("prox", summary, StringComparison.OrdinalIgnoreCase);

        var (category, confidence) = await new LlmCategorizer(model, ["cooking", "web scraping", "sports"]).CategorizeAsync(text, TestContext.Current.CancellationToken);
        Assert.Equal("web scraping", category);
        Assert.Equal(1.0, confidence);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Rag_answers_with_citations_from_a_real_model(string provider)
    {
        var model = Require(provider);
        var kb = new KnowledgeBase(new HashingEmbeddingGenerator(), chunking: new ChunkingOptions { MaxWords = 120 }) { LanguageModel = model };
        var (_, _) = await Scrapy.CrawlAndCollectAsync(new CrawlTestsHelpers.NewsSpider(sandbox.Url("/news/")), s =>
        {
            TestSettings.Quiet(s);
            s.ItemPipelines.Add(new RagIngestionPipeline(kb), 800);
        }, TestContext.Current.CancellationToken);

        var answer = await kb.AskAsync("What does proxy rotation do when a proxy receives bans?", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(answer.Sources);
        Assert.Matches(@"\[\d\]", answer.Answer);
        TestContext.Current.SendDiagnosticMessage($"{provider}: {answer.Answer}");
    }
}

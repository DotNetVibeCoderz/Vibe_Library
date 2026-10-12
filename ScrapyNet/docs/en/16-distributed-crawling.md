# 16. Distributed crawling with Redis

`Gravicode.ScrapyNet.Redis` spreads one crawl over many processes or machines, the way
[scrapy-redis](https://github.com/rmax/scrapy-redis) does for Scrapy: every worker runs the same
spider against one Redis server, which holds the shared request queue and the shared duplicate
filter. Add workers to go faster; stop one and the others carry on.

![Two workers splitting a crawl](../images/gallery-distributed.png)

```bash
dotnet add package Gravicode.ScrapyNet.Redis
```

## Switching a crawl to Redis

```csharp
using ScrapyNet.Redis;

var settings = new Settings()
    .UseRedis("localhost:6379", persist: true)   // SCHEDULER, DUPEFILTER_CLASS, REDIS_URL
    .PushItemsToRedis();                         // optional: items onto "{spider}:items"
await Scrapy.CrawlAsync(new BooksSpider(), settings);
```

Run the same program on as many machines as you like. Requests travel through Redis as JSON, so
callbacks must be named spider methods (as for `JOBDIR`); a request with a lambda callback stays in its
worker's memory and is counted as `scheduler/unserializable`.

## Feeding workers from Redis

A `RedisSpider` takes its start URLs from a Redis list and, when it runs out of work, stays open and
polls the list once a second instead of closing:

```csharp
public sealed class ProductSpider : RedisSpider          // reads "products:start_urls"
{
    public override string Name => "products";

    public override async IAsyncEnumerable<object> Parse(Response response) { ... }
}
```

```bash
redis-cli RPUSH products:start_urls https://example.com/catalog
redis-cli RPUSH products:start_urls '{"url": "https://example.com/sale", "priority": 10, "meta": {"campaign": "sale"}}'
```

Entries are plain URLs or JSON with `url`, `priority`, `method` and `meta`; override
`MakeRequestFromData` for other formats. `RedisCrawlSpider` does the same for a `CrawlSpider`, so start
URLs go through its rules. Set `MAX_IDLE_TIME_BEFORE_CLOSE` (seconds) to let idle workers exit.

## Settings

| Setting | Default | Meaning |
|---|---|---|
| `REDIS_URL` | `localhost:6379` | `host:port`, a StackExchange.Redis configuration string, or `redis://[user:password@]host:port[/db]` (`rediss://` for TLS) |
| `SCHEDULER_QUEUE_CLASS` | `priority` | `priority` (sorted set, honours `Request.Priority`), `fifo` or `lifo` (lists) |
| `SCHEDULER_QUEUE_KEY` | `{spider}:requests` | Queue key; `{spider}` is the spider name |
| `SCHEDULER_DUPEFILTER_KEY` | `{spider}:dupefilter` | Set of request fingerprints |
| `SCHEDULER_PERSIST` | `false` | Keep queue and filter when the spider closes; set it with several workers, or the first to finish clears the others' queue |
| `SCHEDULER_FLUSH_ON_START` | `false` | Start from scratch: clear queue and filter on open |
| `REDIS_START_URLS_KEY` | `{spider}:start_urls` | Where `RedisSpider` reads start entries |
| `REDIS_START_URLS_AS_SET` | `false` | Read start entries from a set (`SPOP`) instead of a list |
| `REDIS_START_URLS_BATCH_SIZE` | `CONCURRENT_REQUESTS` | Entries fetched per idle tick |
| `MAX_IDLE_TIME_BEFORE_CLOSE` | `0` | Seconds a Redis-fed spider waits for new entries; 0 waits forever |
| `REDIS_ITEMS_KEY` | `{spider}:items` | List `RedisPipeline` pushes JSON items to |

Stats: `scheduler/enqueued/redis`, `scheduler/dequeued/redis`, `redis/start_urls`, and the usual
`dupefilter/filtered`.

## How it maps to scrapy-redis

| scrapy-redis | Scrapy.Net |
|---|---|
| `SCHEDULER = "scrapy_redis.scheduler.Scheduler"` | `settings.UseRedis(...)` (sets `SCHEDULER` to `RedisScheduler`) |
| `DUPEFILTER_CLASS = "scrapy_redis.dupefilter.RFPDupeFilter"` | `RedisDupeFilter` (set by `UseRedis`) |
| `scrapy_redis.spiders.RedisSpider` / `RedisCrawlSpider` | `RedisSpider` / `RedisCrawlSpider` |
| `scrapy_redis.pipelines.RedisPipeline` | `RedisPipeline` (`settings.PushItemsToRedis()`) |
| `PriorityQueue`, `FifoQueue`, `LifoQueue` | `SCHEDULER_QUEUE_CLASS = priority / fifo / lifo` |

Like scrapy-redis, a worker that pops a request and then dies loses that one request; use
`RETRY_*` and periodic re-seeding for at-least-once guarantees on critical crawls.

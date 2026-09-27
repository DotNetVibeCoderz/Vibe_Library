# Feature List Scrapy Python

> Dokumen ini merangkum fitur utama **Scrapy**, framework Python untuk web crawling dan web scraping, serta rekomendasi fitur untuk membangun crawler platform di atas Scrapy.

## 1. Core Crawling Features

- Spider
  - Start URLs
  - Crawling rules
  - Page parsing
  - Follow links
  - Custom callbacks
- Asynchronous crawling
- Request & Response API
  - GET / POST requests
  - Custom HTTP methods
  - Headers dan request body
  - Request metadata
  - Callback dan errback
- Link Extractor
  - Domain/path filtering
  - Allow/deny patterns
- Scheduler
  - Request queue
  - Request priority
  - Duplicate request filtering
- Pause dan resume crawl jobs

## 2. Data Extraction

### Selectors
- CSS Selector
- XPath
- Attribute extraction
- Text extraction
- Nested selectors
- Regex extraction

Contoh:

```python
title = response.css("h1::text").get()
price = response.xpath("//span[@class='price']/text()").get()
```

### Items
- Mendefinisikan struktur data hasil scraping
- Field-based data model

```python
class ProductItem(scrapy.Item):
    name = scrapy.Field()
    price = scrapy.Field()
    url = scrapy.Field()
```

### Item Loaders
- Mapping scraped data ke Item
- Input processor
- Output processor
- Normalisasi data
- Reusable transformations

## 3. Data Processing Pipeline

- Data cleaning
- Data validation
- Duplicate removal
- Text normalization
- Price normalization
- Data type conversion
- Business rules
- Database persistence
- API forwarding
- File processing

```text
Spider
   -> Extract Item
   -> Validation Pipeline
   -> Cleaning Pipeline
   -> Duplicate Pipeline
   -> Database Pipeline
```

## 4. Data Export

Format umum:
- JSON
- JSON Lines / JSONL
- CSV
- XML

Contoh:

```bash
scrapy crawl products -o products.json
scrapy crawl products -o products.csv
```

Integrasi melalui pipeline dapat dikembangkan untuk:
- PostgreSQL
- MySQL
- SQL Server
- MongoDB
- Elasticsearch
- REST API
- Object Storage

## 5. Downloader Middleware

- Custom User-Agent
- Random User-Agent
- Custom Referer
- Authorization headers
- API keys
- Custom cookies
- Proxy configuration
- Proxy rotation
- Per-domain proxy
- Authentication/session middleware

## 6. Cookies & Session

- Send/receive cookies
- Cookie persistence
- Multiple cookie jars
- Session management
- Login session crawling

## 7. Retry & Error Handling

- Automatic retry
- HTTP status handling
- Timeout handling
- DNS/connection error handling
- Maximum retry configuration
- Retry priority
- Custom errback

## 8. AutoThrottle

- Dynamic download delay
- Target concurrency
- Maximum delay
- Per-domain throttling

## 9. Concurrent Crawling

- Global concurrency
- Per-domain concurrency
- Per-IP concurrency
- Download delay
- Request priority

## 10. Duplicate Request Filtering

- Request fingerprints
- Duplicate detection
- Request deduplication
- Force re-crawl
- Persistent visited URLs

## 11. Files & Images

- PDF/document crawler
- Image crawler
- Asset downloader
- Dataset collection
- File/image processing pipeline

## 12. Scrapy Shell

Interactive environment untuk menguji selectors dan extraction logic.

```bash
scrapy shell https://example.com
```

## 13. Logging

- Spider start/complete
- URLs crawled
- HTTP errors
- Retries
- Items scraped
- Pipeline errors
- Download errors

## 14. Statistics

- Total requests
- Total responses
- HTTP status distribution
- Items scraped
- Bytes downloaded
- Retry count
- Error count
- Elapsed time

## 15. Extensions & Plugin Architecture

Komponen yang dapat diperluas/customize:
- Scheduler
- Downloader
- Spider middleware
- Downloader middleware
- Pipelines
- Feed exporters
- Extensions
- Monitoring

## 16. Signals / Event System

Contoh event lifecycle:

```text
Spider Opened
     -> Request Sent
     -> Response Received
     -> Item Scraped
     -> Spider Closed
```

## 17. Settings & Configuration

Konfigurasi dapat mencakup:
- Concurrency
- Timeout
- Download delay
- Middleware
- Pipelines
- Logging
- Retry
- Cookies
- Feed exports
- Extensions

Environment dapat dipisahkan, misalnya:

```text
settings.py
settings_dev.py
settings_test.py
settings_prod.py
```

## 18. Command Line Interface

```bash
scrapy startproject crawler
scrapy genspider product example.com
scrapy crawl product
scrapy shell https://example.com
scrapy list
scrapy crawl product -o result.json
```

## 19. JavaScript-heavy Websites

Untuk halaman yang membutuhkan render JavaScript, Scrapy dapat dikombinasikan dengan browser integration seperti `scrapy-playwright`.

```text
Scrapy
  |-- HTTP Page -> Normal Downloader
  |-- JS Page -> Playwright -> Render Page -> Scrapy Parser
```

## 20. Production-Level Platform Features

### Spider Management
- Create/edit/delete spider
- Enable/disable spider
- Clone spider
- Spider versioning

### Job Management
- Run now
- Stop
- Pause/resume
- Cancel
- Retry
- Job history

### Scheduler
- Every N minutes
- Hourly
- Daily
- Weekly
- Monthly
- Cron expressions

### Target Management
- Base URL
- Allowed domains
- Start URLs
- Crawl depth
- Include patterns
- Exclude patterns

### Credential & Secret Management
- Username/password
- API key
- Bearer token
- Cookies
- Proxy credentials
- Encrypted secret storage

### Data Management
- Raw HTML
- Extracted data
- Files/images
- Metadata
- Crawl timestamps
- Source URLs
- HTTP status

### Monitoring Dashboard
- Running jobs
- Completed/failed jobs
- Scraped item count
- Request rate
- Spider status
- Error rate

### Notifications
- Job completed/failed
- Spider errors
- Target inaccessible
- Zero items detected
- Abnormal item decrease
- Authentication expired
- Email, Teams, Slack, Webhook integrations

## 21. AI / LLM Integration

Scrapy dapat dijadikan ingestion layer untuk AI/RAG:

```text
Website
   -> Scrapy
   -> Clean / Normalize
   -> Content Extraction
   -> Chunking
   -> Embedding Model
   -> Vector Database
   -> RAG / AI Agent
```

Fitur tambahan:
- Clean article extraction
- Metadata extraction
- Duplicate content detection
- Document chunking
- Embedding generation
- Semantic indexing
- Vector database integration
- Incremental re-crawl
- Content change detection
- AI-based categorization
- AI summarization
- Knowledge-base ingestion

## 22. Rekomendasi Modul Aplikasi

1. Dashboard
2. Project Management
3. Target Website Management
4. Spider Management
5. Scraping Rules
6. CSS/XPath Selector Builder
7. Request Management
8. Authentication
9. Cookie & Session Management
10. Proxy Management
11. Scheduler
12. Crawl Job Management
13. Retry & Error Handling
14. Data Pipeline
15. Export
16. File/Image Downloader
17. Raw Content Storage
18. Extracted Data Storage
19. Logs
20. Metrics & Statistics
21. Monitoring
22. Notifications
23. User & Role Management
24. API
25. Webhook
26. AI/RAG Integration
27. System Settings

## Recommended Architecture

```text
             WEB ADMIN / API
                    |
        +-----------+-----------+
        |                       |
    Scheduler              Job Manager
        |                       |
        +-----------+-----------+
                    |
               Scrapy Engine
          +---------+---------+
          |         |         |
       Spider     Spider    Spider
          |         |         |
          +---------+---------+
                    |
              Data Pipeline
                    |
       +------------+------------+
       |            |            |
   Database       Files     Search/Vector
```

Dalam arsitektur ini, **Scrapy berfungsi sebagai crawling engine**, sedangkan aplikasi di atasnya berfungsi sebagai **control plane / management platform**.

## Referensi

- Scrapy Documentation: https://docs.scrapy.org/
- Scrapy at a Glance: https://docs.scrapy.org/en/latest/intro/overview.html
- Scrapy Official Site: https://www.scrapy.org/

## Catatan

Istilah "scrappy" pada permintaan awal diasumsikan merujuk pada **Scrapy**, framework web crawling dan web scraping untuk Python.

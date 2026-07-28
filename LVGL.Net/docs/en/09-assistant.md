# Jack The Code Bender — the design assistant

An AI assistant built into the designer. It designs LVGL screens, writes the .NET code around
them, and can hand a finished layout straight back to the canvas.

Open it with **Ask Jack** in the designer toolbar.

## What it can do

| Capability | Detail |
|---|---|
| Design layouts | Produces a validated `.lvgl.json` you can open in the designer with one click |
| Generate code | The same `CSharpUiGenerator` the Export C# button uses, plus event-handler stubs |
| Search the web | Tavily, when a key is configured |
| Read pages and files | Fetches a URL and strips it to readable text, or downloads a text file verbatim |
| Dates and arithmetic | So it does not do either in its head |
| See images | Attach a screenshot or mockup and ask for it to be reproduced |

## Providers

Four are supported, selectable per chat:

| Provider | Configure | Notes |
|---|---|---|
| OpenAI | `OPENAI_API_KEY` or `Assistant:OpenAI:ApiKey` | Default |
| Anthropic | `ANTHROPIC_API_KEY` or `Assistant:Anthropic:ApiKey` | See the temperature note below |
| Gemini | `GEMINI_API_KEY` or `Assistant:Gemini:ApiKey` | |
| Ollama | `Assistant:Ollama:Endpoint` | Runs locally, no key needed |

Semantic Kernel supplies the connectors for OpenAI, Gemini and Ollama. It has none for Anthropic,
so LVGL.Net implements `IChatCompletionService` over the official Anthropic SDK — not an
OpenAI-compatible endpoint, because Claude's request shape genuinely differs and a shim would hide
exactly the parts that matter.

### The temperature caveat

Anthropic **removed the sampling parameters** on Claude Opus 5, Opus 4.8, Opus 4.7, Sonnet 5 and
Fable 5. Sending a `temperature` to one of those models is rejected with HTTP 400 — it is not
ignored.

`Assistant:Temperature` applies to every provider, so the Anthropic connector detects the model
family and leaves the value out of the request rather than failing it. The chat window's status bar
says `temperature ignored on this model` when that happens. Older Claude models still receive it
normally. Use the model's effort setting to trade quality against cost instead.

## Configuration

Everything lives in the designer's `app.config`, under `appSettings` with an `Assistant:` prefix.
An environment variable of the documented name always wins over a key in the file, so you can leave
the keys blank and keep them out of source control.

```xml
<add key="Assistant:Provider" value="Anthropic" />
<add key="Assistant:Temperature" value="0.7" />
<add key="Assistant:MaxTokens" value="4096" />
<add key="Assistant:HistoryTurnLimit" value="40" />
<add key="Assistant:EnableFunctionCalling" value="true" />
<add key="Assistant:MaxToolIterations" value="8" />
<add key="Assistant:SystemPrompt" value="" />
<add key="Assistant:Anthropic:Model" value="claude-opus-5" />
<add key="Assistant:TavilyApiKey" value="" />
```

Leaving `Assistant:SystemPrompt` empty uses the built-in persona, which is worth keeping: it names
the pitfalls specific to this wrapper — the encoded coordinates, widget lifetime, thread affinity —
that a model cannot infer from the class names, and that produce code which compiles and then
misbehaves.

## Chats

Each conversation is a chat, stored as one JSON file under `%APPDATA%\LVGL.Net\assistant`.

- **New** starts a fresh chat.
- **Reset** clears the messages but keeps the chat, its title and its provider.
- **Delete** removes it permanently.

Each chat remembers its own provider, so you can keep one on a local Ollama model for quick
questions and another on a frontier model for hard design work.

## Attachments

**Images** are sent to the model as image content, so it can actually see them. Attach a screenshot
and ask for it to be reproduced as a layout.

**Documents** are referenced by URL in the message text; the model decides whether it needs the
contents and fetches them with the read-file tool. That avoids every attachment costing tokens
whether or not it is relevant.

Files are copied into the session directory and served from a loopback HTTP host on `127.0.0.1`.
Note the consequence: **a hosted model cannot reach a loopback URL.** That is exactly why images are
additionally sent as inline bytes — the URL is what you and the transcript see, the bytes are what
the model receives. A locally-running Ollama model can fetch the document URLs; a hosted one will
tell you it cannot.

If the local host cannot bind a port, attachments still work and fall back to `file://` URLs.

## Prompt templates

The **Prompts** button opens a gallery of worked examples covering layout design, backend code,
code generation, screenshot reproduction, deployment, review, debugging, performance and theming.
Pick one, fill in the bracketed placeholders, and send.

## Tools

The model calls these itself; you do not invoke them directly.

| Tool | What it does |
|---|---|
| `lvgl_design-describe_widgets` | The widget set and the properties each accepts |
| `lvgl_design-layout_template` | A valid starting layout: blank, dashboard, form or chart |
| `lvgl_design-create_layout` | Validates a layout and offers it to the designer |
| `lvgl_design-validate_layout` | Checks a layout without keeping it |
| `lvgl_design-generate_csharp` | The generated partial class |
| `lvgl_design-generate_event_handlers` | The hand-written half, with a stub per interactive widget |
| `tavily-search` | Internet search |
| `web-scrape_page` | Fetches a page as readable text |
| `web-read_file` | Downloads a text file verbatim |
| `web-http_head` | Checks whether a URL is reachable |
| `time-*` | Current date and time, date arithmetic, durations |
| `math-*` | Expression evaluation, percentages, pixel scaling |

`create_layout` validates against the real document model before answering, so a malformed layout
comes back as a list of problems the model can fix rather than as plausible-looking JSON that will
not open.

## Security notes

Worth understanding before pointing the assistant at arbitrary content:

- **Fetched pages and search results are untrusted.** They can contain text aimed at the model
  rather than at you. Both tools wrap their output in a marked block that says so. This reduces the
  risk; it does not eliminate it. Treat an assistant that has just read a hostile page with the same
  suspicion you would treat the page.
- **The web tools refuse private and loopback addresses**, so a fetched page cannot redirect them
  into your own network or at a cloud metadata endpoint.
- **The math tool is a parser, not an interpreter.** It understands numbers, operators and a fixed
  function list; anything else is a parse error. It cannot be talked into executing code.
- **The attachment host serves one directory** and rejects any request containing a path separator
  or traversal segment.
- **API keys are never sent to the model** — they are used only to authenticate the request.

## Troubleshooting

**"No API key for X"** — set the environment variable named in the message, or fill in the matching
`app.config` key, then reopen the chat window.

**The chat area says WebView2 is missing** — install the Microsoft Edge WebView2 Runtime. It ships
with Windows 11, so this is unusual. Everything except the rendered transcript still works.

**The status bar says `web search off`** — no `Assistant:TavilyApiKey`, so the search tool is not
registered at all. That is deliberate: an advertised tool that always fails wastes a round trip and
teaches the model to distrust it.

**A reply stops with "I stopped after N tool rounds"** — the model looped without converging. Narrow
the request, or raise `Assistant:MaxToolIterations`.

**Ollama replies are poor or it ignores tools** — smaller local models are weak at function calling.
Try a larger one, or turn `Assistant:EnableFunctionCalling` off and ask it direct questions.

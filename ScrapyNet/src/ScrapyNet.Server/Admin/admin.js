// Scrapy.Net Console: a client for the Scrapy.Net platform REST API. Every value shown comes from
// the API; scraped content is untrusted, so the DOM is only ever built with textContent.

const API = document.querySelector('meta[name="scrapynet-api"]')?.content || "/api";
const ROLES = { Viewer: 0, Operator: 1, Admin: 2 };
const POLL_MS = 2000;
const SVG = "http://www.w3.org/2000/svg";

const state = { key: null, me: null, timer: null, view: null };

// ---- storage (per-viewer conveniences only; may be unavailable) --------------------------------
const store = {
  get(name) { try { return sessionStorage.getItem(name) ?? localStorage.getItem(name); } catch { return null; } },
  set(name, value, persist) { try { (persist ? localStorage : sessionStorage).setItem(name, value); } catch { /* private mode */ } },
  clear(name) { try { sessionStorage.removeItem(name); localStorage.removeItem(name); } catch { /* ignore */ } },
};

// ---- DOM helpers --------------------------------------------------------------------------------
function h(tag, attrs = {}, ...children) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs ?? {})) {
    if (v === null || v === undefined || v === false) continue;
    if (k === "class") el.className = v;
    else if (k.startsWith("on")) el.addEventListener(k.slice(2), v);
    else if (k === "dataset") Object.assign(el.dataset, v);
    else el.setAttribute(k, v === true ? "" : String(v));
  }
  for (const c of children.flat(Infinity)) {
    if (c === null || c === undefined || c === false) continue;
    el.append(c instanceof Node ? c : document.createTextNode(String(c)));
  }
  return el;
}
function s(tag, attrs = {}, ...children) {
  const el = document.createElementNS(SVG, tag);
  for (const [k, v] of Object.entries(attrs)) if (v !== null && v !== undefined) el.setAttribute(k, String(v));
  for (const c of children.flat()) if (c) el.append(c instanceof Node ? c : document.createTextNode(String(c)));
  return el;
}
const $ = (sel) => document.querySelector(sel);

const nf = new Intl.NumberFormat();
const compact = new Intl.NumberFormat(undefined, { notation: "compact", maximumFractionDigits: 1 });
const num = (n) => (n ?? 0) >= 10000 ? compact.format(n) : nf.format(n ?? 0);
const pct = (x) => `${((x ?? 0) * 100).toFixed((x ?? 0) < 0.1 ? 1 : 0)}%`;
const time = (t) => t ? new Date(t).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" }) : "—";
const ago = (t) => {
  if (!t) return "never";
  const sec = Math.round((Date.now() - new Date(t)) / 1000);
  if (sec < 60) return `${sec}s ago`;
  if (sec < 3600) return `${Math.round(sec / 60)} min ago`;
  if (sec < 86400) return `${Math.round(sec / 3600)} h ago`;
  return `${Math.round(sec / 86400)} d ago`;
};
const duration = (a, b) => {
  if (!a) return "—";
  const sec = Math.max(0, Math.round(((b ? new Date(b) : new Date()) - new Date(a)) / 1000));
  return sec < 60 ? `${sec}s` : sec < 3600 ? `${Math.floor(sec / 60)}m ${sec % 60}s` : `${Math.floor(sec / 3600)}h ${Math.floor(sec % 3600 / 60)}m`;
};

const can = (role) => state.me && ROLES[state.me.role] >= ROLES[role];

// ---- API ----------------------------------------------------------------------------------------
async function api(path, { method = "GET", body } = {}) {
  const response = await fetch(API + path, {
    method,
    headers: { "X-Api-Key": state.key, ...(body !== undefined ? { "Content-Type": "application/json" } : {}) },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  if (response.status === 401) { signOut("Your API key was not accepted. Sign in again."); throw new Error("Unauthorized"); }
  const text = await response.text();
  const data = text ? (() => { try { return JSON.parse(text); } catch { return text; } })() : null;
  if (!response.ok) {
    const message = data?.error ?? data?.title ?? (response.status === 403 ? "Your role does not allow this." : `Request failed (${response.status}).`);
    throw new Error(message);
  }
  return data;
}

function toast(message, error = false) {
  const t = $("#toast");
  t.textContent = message;
  t.className = "toast show" + (error ? " error" : "");
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => (t.className = "toast"), error ? 6000 : 3000);
}

async function act(label, fn) {
  try {
    const result = await fn();
    if (label) toast(label);
    return result;
  } catch (e) {
    if (e.message !== "Unauthorized") toast(e.message, true);
    return undefined;
  }
}

// ---- status vocabulary --------------------------------------------------------------------------
const JOB_BADGE = {
  Running: ["run", "Running"], Pending: ["wait", "Queued"], Paused: ["wait", "Paused"], Completed: ["ok", "Completed"],
  Failed: ["bad", "Failed"], Stopped: ["off", "Stopped"], Cancelled: ["off", "Cancelled"],
};
const badge = (status) => { const [cls, label] = JOB_BADGE[status] ?? ["off", status ?? "—"]; return h("span", { class: `badge ${cls}` }, label); };
const SEVERITY = { Info: ["ok", "Info"], Warning: ["wait", "Warning"], Critical: ["bad", "Critical"] };
const words = (s) => String(s ?? "").replace(/([a-z])([A-Z])/g, "$1 $2");

// ---- shell, sign-in, routing --------------------------------------------------------------------
async function signIn(key, remember) {
  state.key = key;
  const me = await api("/me");
  state.me = me;
  store.set("scrapynet.key", key, remember);
  $("#signin").hidden = true;
  $("#shell").hidden = false;
  $("#who-name").textContent = me.name;
  $("#who-role").textContent = me.role;
  route();
}

function signOut(message) {
  clearInterval(state.timer);
  store.clear("scrapynet.key");
  state.key = state.me = null;
  $("#shell").hidden = true;
  $("#signin").hidden = false;
  $("#signin-error").textContent = message ?? "";
  $("#api-key").value = "";
  $("#api-key").focus();
}

function setLive(ok, text) {
  const live = $("#live");
  live.className = "live " + (ok ? "on" : "down");
  $("#live-text").textContent = text;
}

const VIEWS = { dashboard, spiders, spider: spiderEditor, jobs, job: jobDetail, schedules, builder, alerts };

function route() {
  if (!state.me) return;
  clearInterval(state.timer);
  const [name = "dashboard", ...rest] = location.hash.replace(/^#\/?/, "").split("/").map(decodeURIComponent);
  const view = VIEWS[name] ? name : "dashboard";
  const section = view === "spider" ? "spiders" : view === "job" ? "jobs" : view;
  for (const a of document.querySelectorAll(".rail a")) {
    if (a.dataset.view === section) a.setAttribute("aria-current", "page");
    else a.removeAttribute("aria-current");
  }
  $("#rail").classList.remove("open");
  $("#nav-toggle").setAttribute("aria-expanded", "false");
  state.view = view;
  const main = $("#main");
  main.replaceChildren();
  VIEWS[view](main, ...rest);
  main.focus({ preventScroll: true });
}

function poll(fn) {
  const tick = async () => {
    if (document.hidden) return;
    try { await fn(); setLive(true, "Live"); } catch (e) { if (e.message !== "Unauthorized") setLive(false, "Disconnected — retrying"); }
  };
  tick();
  state.timer = setInterval(tick, POLL_MS);
}

function pageHead(title, subtitle, ...actions) {
  return h("div", { class: "page-head" },
    h("div", {}, h("h1", {}, title), subtitle ? h("p", {}, subtitle) : null),
    actions.length ? h("div", { class: "actions" }, actions) : null);
}

function emptyRow(cols, title, text) {
  return h("tr", {}, h("td", { colspan: cols, class: "empty" }, h("strong", {}, title), text));
}

// ---- signature: the orb web ---------------------------------------------------------------------
// One running job as an orb web. 24 spokes are the last 24 one-second samples, newest at twelve
// o'clock and older going clockwise; the silk reaches out as far as that second's pages fetched,
// relative to the busiest second shown. The web turns rust when more than 5% of responses are errors.
function orbWeb(series, metrics) {
  const N = 24, R = 40, C = 48;
  const samples = (series ?? []).slice(-(N + 1));
  const rates = [];
  for (let i = 1; i < samples.length; i++) rates.push(Math.max(0, samples[i].responses - samples[i - 1].responses));
  while (rates.length < N) rates.unshift(0);
  const max = Math.max(1, ...rates);
  const angle = (i) => -Math.PI / 2 + ((N - 1 - i) / N) * Math.PI * 2;
  const point = (i, r) => [C + Math.cos(angle(i)) * r, C + Math.sin(angle(i)) * r];
  const svg = s("svg", { viewBox: "0 0 96 96", class: "orb" + ((metrics?.errorRate ?? 0) > 0.05 ? " has-errors" : ""), role: "img",
    "aria-label": `Pages per second over the last ${N} seconds; latest ${rates[N - 1]}, peak ${max}` });
  for (const r of [R / 3, (2 * R) / 3, R]) {
    svg.append(s("polygon", { class: "ring", points: Array.from({ length: N }, (_, i) => point(i, r).join(",")).join(" ") }));
  }
  for (let i = 0; i < N; i += 2) { const [x, y] = point(i, R); svg.append(s("line", { class: "spoke", x1: C, y1: C, x2: x, y2: y })); }
  const silk = rates.map((v, i) => point(i, 4 + (v / max) * (R - 4)).join(",")).join(" ");
  svg.append(s("polygon", { class: "silk", points: silk }));
  const [nx, ny] = point(N - 1, 4 + (rates[N - 1] / max) * (R - 4));
  svg.append(s("circle", { class: "now", cx: nx, cy: ny, r: 4 }));
  svg.append(s("circle", { class: "hub", cx: C, cy: C, r: 2.5 }));
  return svg;
}

// ---- charts -------------------------------------------------------------------------------------
// Pages fetched per minute for one job, from its one-second samples: one series, so no legend;
// a crosshair tooltip on hover and the latest value labelled at the line's end.
function throughputChart(series, width = 640, finished = false) {
  const wrap = h("div", { class: "chart" });
  const pts = [];
  const raw = series ?? [];
  for (let i = 5; i < raw.length; i += 5) {
    const a = raw[i - 5], b = raw[i];
    const sec = (new Date(b.time) - new Date(a.time)) / 1000 || 5;
    pts.push({ t: new Date(b.time), v: Math.max(0, (b.responses - a.responses) * 60 / sec) });
  }
  if (pts.length < 2) {
    wrap.append(h("p", { class: "muted" }, finished ? "This run was too short to chart; its totals are under Run." : "Collecting samples — the chart appears after about ten seconds of crawling."));
    return wrap;
  }
  // Drawn at the width it is shown at, so text stays 11px instead of scaling with the panel.
  const W = Math.max(280, Math.round(width)), H = 190, L = 40, Rp = 70, T = 12, B = 24;
  const maxV = niceMax(Math.max(...pts.map((p) => p.v)));
  const x = (i) => L + (i / (pts.length - 1)) * (W - L - Rp);
  const y = (v) => T + (1 - v / maxV) * (H - T - B);
  const svg = s("svg", { viewBox: `0 0 ${W} ${H}`, role: "img", "aria-label": `Pages per minute; latest ${Math.round(pts.at(-1).v)}` });
  for (let k = 0; k <= 4; k++) {
    const v = (maxV / 4) * k;
    svg.append(s("line", { class: "grid-line", x1: L, x2: W - Rp, y1: y(v), y2: y(v) }));
    svg.append(s("text", { class: "axis-text", x: L - 8, y: y(v) + 4, "text-anchor": "end" }, compact.format(v)));
  }
  const first = pts[0].t, last = pts.at(-1).t;
  for (const [i, anchor] of [[0, "start"], [pts.length - 1, "end"]])
    svg.append(s("text", { class: "axis-text", x: x(i), y: H - 6, "text-anchor": anchor }, (i ? last : first).toLocaleTimeString(undefined, { timeStyle: "short" })));
  const line = pts.map((p, i) => `${i ? "L" : "M"}${x(i).toFixed(1)},${y(p.v).toFixed(1)}`).join("");
  svg.append(s("path", { class: "area", d: `${line}L${x(pts.length - 1)},${y(0)}L${x(0)},${y(0)}Z` }));
  svg.append(s("path", { class: "line", d: line }));
  const lx = x(pts.length - 1), ly = y(pts.at(-1).v);
  svg.append(s("circle", { class: "end-dot", cx: lx, cy: ly, r: 4 }));
  svg.append(s("text", { class: "end-label", x: lx + 8, y: ly + 4 }, `${num(Math.round(pts.at(-1).v))}/min`));
  const cross = s("line", { class: "cross", y1: T, y2: H - B, visibility: "hidden" });
  svg.append(cross);
  const tip = h("div", { class: "tooltip", hidden: true });
  svg.addEventListener("pointermove", (e) => {
    const box = svg.getBoundingClientRect();
    const sx = ((e.clientX - box.left) / box.width) * W;
    const i = Math.max(0, Math.min(pts.length - 1, Math.round(((sx - L) / (W - L - Rp)) * (pts.length - 1))));
    cross.setAttribute("x1", x(i)); cross.setAttribute("x2", x(i)); cross.setAttribute("visibility", "visible");
    tip.hidden = false;
    tip.textContent = `${pts[i].t.toLocaleTimeString()} · ${num(Math.round(pts[i].v))} pages/min`;
    tip.style.left = `${(x(i) / W) * 100}%`;
    tip.style.top = `${(y(pts[i].v) / H) * box.height}px`;
  });
  svg.addEventListener("pointerleave", () => { cross.setAttribute("visibility", "hidden"); tip.hidden = true; });
  wrap.append(svg, tip);
  return wrap;
}

function niceMax(v) {
  if (v <= 0) return 10;
  const p = 10 ** Math.floor(Math.log10(v));
  for (const m of [1, 2, 2.5, 5, 10]) if (m * p >= v) return m * p;
  return 10 * p;
}

// HTTP outcomes as one stacked bar: 2xx, 3xx, 4xx–5xx, with a legend that carries the counts.
function statusBar(statusCounts) {
  const groups = { s2: 0, s3: 0, s4: 0 };
  for (const [code, count] of Object.entries(statusCounts ?? {})) {
    const c = Number(code);
    groups[c < 300 ? "s2" : c < 400 ? "s3" : "s4"] += count;
  }
  const total = groups.s2 + groups.s3 + groups.s4;
  const labels = { s2: "2xx success", s3: "3xx redirect", s4: "4xx–5xx error" };
  const bar = h("div", { class: "statusbar", role: "img", "aria-label": Object.entries(groups).map(([k, v]) => `${labels[k]} ${v}`).join(", ") });
  if (total) for (const [k, v] of Object.entries(groups)) if (v) { const seg = h("span", { class: k }); seg.style.flex = String(v); seg.title = `${labels[k]}: ${nf.format(v)}`; bar.append(seg); }
  return h("div", {}, bar,
    h("ul", { class: "legend" }, Object.entries(groups).map(([k, v]) => h("li", {}, h("span", { class: `key ${k}` }), `${labels[k]} `, h("b", {}, nf.format(v))))));
}

// ---- dashboard ----------------------------------------------------------------------------------
function dashboard(main) {
  const heroValue = h("p", { class: "hero-figure" }, "—");
  const tiles = h("div", { class: "tiles" });
  const runs = h("div", { class: "runs" });
  const chartPanel = h("div", {});
  const spidersBody = h("tbody");
  let focusJob = null;

  main.append(
    pageHead("What is crawling now", "Live view of every running job, refreshed every two seconds."),
    h("section", { class: "panel hero", "aria-label": "Summary" },
      h("div", {}, heroValue, h("p", { class: "hero-label" }, "requests in the last minute, all running jobs")), tiles),
    h("div", { class: "grid grid-dash" },
      h("section", { class: "panel", "aria-labelledby": "runs-title" },
        h("div", { class: "panel-head" }, h("h2", { id: "runs-title" }, "Running jobs"),
          h("p", {}, "Each web: pages per second over the last 24 seconds, newest at twelve o'clock.")),
        runs),
      h("section", { class: "panel" }, chartPanel)),
    h("section", { class: "panel" },
      h("div", { class: "panel-head" }, h("h2", {}, "Spiders"), h("p", {}, "Last run of each spider")),
      h("div", { class: "table-wrap" }, h("table", {},
        h("thead", {}, h("tr", {}, h("th", {}, "Spider"), h("th", {}, "Last run"), h("th", {}, "Status"), h("th", { class: "num" }, "Items"), h("th", { class: "num" }, "Runs"), h("th", { class: "num" }, "Failures"))),
        spidersBody))));
  main.querySelector(".grid-dash").style.marginTop = "16px";
  main.lastElementChild.style.marginTop = "16px";

  poll(async () => {
    const d = await api("/dashboard");
    heroValue.textContent = num(d.requestRatePerMinute);
    tiles.replaceChildren(
      tile("Running", d.running.length), tile("Completed jobs", d.completedJobs),
      tile("Failed jobs", d.failedJobs), tile("Items, last 24 h", d.itemsLast24Hours),
      tile("Error rate", pct(d.errorRate)));

    if (!d.running.length) {
      runs.replaceChildren(h("div", { class: "empty" }, h("strong", {}, "No job is running"),
        can("Operator") ? "Start one from Spiders, or wait for a schedule." : "Jobs appear here as soon as one starts."));
    } else {
      runs.replaceChildren(...d.running.map(({ job, metrics }) => runRow(job, metrics, () => { focusJob = job.id; })));
    }
    const focus = d.running.find((r) => r.job.id === focusJob) ?? d.running[0];
    chartPanel.replaceChildren(...[
      h("div", { class: "panel-head" }, h("h2", {}, "Throughput"), h("p", {}, focus ? `${focus.job.spiderName} · pages per minute, from 5-second windows` : "No running job")),
      focus ? throughputChart(focus.metrics.series, chartPanel.clientWidth) : h("p", { class: "muted" }, "Pages per minute for a running job appear here."),
      focus ? h("div", { class: "panel-head" }, h("h3", {}, "Responses by status")) : null,
      focus ? statusBar(focus.metrics.statusCounts) : null].filter(Boolean));

    spidersBody.replaceChildren(...(d.spiders.length ? d.spiders.map((sp) => h("tr", { class: "clickable", onclick: () => (location.hash = `#/spider/${encodeURIComponent(sp.spiderId)}`) },
      h("td", {}, h("div", { class: "cell-main" }, sp.name), sp.enabled ? null : h("div", { class: "cell-sub" }, "Disabled")),
      h("td", {}, ago(sp.lastRun)),
      h("td", {}, sp.lastStatus ? badge(sp.lastStatus) : h("span", { class: "muted" }, "Never run")),
      h("td", { class: "num" }, num(sp.lastItems)), h("td", { class: "num" }, num(sp.runs)), h("td", { class: "num" }, num(sp.failures))))
      : [emptyRow(6, "No spiders yet", can("Admin") ? "Create one in Spiders or with the Selector builder." : "An admin can add spiders.")]));
  });

  function tile(label, value) {
    return h("div", { class: "tile" }, h("p", { class: "tile-label" }, label), h("p", { class: "tile-value" }, typeof value === "number" ? num(value) : value));
  }
}

function runRow(job, m, onFocus) {
  const paused = job.status === "Paused";
  return h("article", { class: "run", onmouseenter: onFocus, onfocusin: onFocus },
    orbWeb(m.series, m),
    h("div", {},
      h("h3", { class: "run-name" }, h("a", { href: `#/job/${encodeURIComponent(job.id)}` }, job.spiderName)),
      h("p", { class: "run-meta" }, badge(job.status), ` · started ${ago(job.startedAt)} · ${job.trigger}`),
      h("p", { class: "counts" },
        h("span", {}, h("b", {}, num(m.responses)), " pages"), h("span", {}, h("b", {}, num(m.items)), " items"),
        h("span", {}, h("b", {}, num(m.errors)), " errors"), h("span", {}, h("b", {}, num(Math.round(m.responsesPerMinute))), " in the last minute"))),
    can("Operator") ? h("div", { class: "run-actions" },
      h("button", { class: "btn btn-sm", onclick: () => act(paused ? "Resumed" : "Paused", () => api(`/jobs/${job.id}/${paused ? "resume" : "pause"}`, { method: "POST" })) }, paused ? "Resume" : "Pause"),
      h("button", { class: "btn btn-sm btn-danger", onclick: () => act("Stopping — the queue is kept for a resume", () => api(`/jobs/${job.id}/stop`, { method: "POST" })) }, "Stop")) : null);
}

// ---- spiders ------------------------------------------------------------------------------------
async function spiders(main) {
  const body = h("tbody");
  main.append(pageHead("Spiders", "Every spider the platform can run: declarative rules or code.",
    can("Admin") ? h("a", { class: "btn btn-primary", href: "#/spider/new" }, "New spider") : null),
    h("section", { class: "panel" }, h("div", { class: "table-wrap" }, h("table", {},
      h("thead", {}, h("tr", {}, h("th", {}, "Spider"), h("th", {}, "Kind"), h("th", { class: "num" }, "Version"), h("th", {}, "State"), h("th", {}, "Updated"), h("th", {}, ""))),
      body))));
  const load = async () => {
    const list = await act(null, () => api("/spiders"));
    if (!list) return;
    body.replaceChildren(...(list.length ? list.map((sp) => h("tr", {},
      h("td", {}, h("a", { class: "cell-main", href: `#/spider/${encodeURIComponent(sp.id)}` }, sp.name), h("div", { class: "cell-sub mono" }, sp.id),
        sp.description ? h("div", { class: "cell-sub" }, sp.description) : null),
      h("td", {}, sp.spiderType ? "Code" : "Declarative"),
      h("td", { class: "num" }, `v${sp.version}`),
      h("td", {}, h("span", { class: `badge ${sp.enabled ? "ok" : "off"}` }, sp.enabled ? "Enabled" : "Disabled")),
      h("td", {}, ago(sp.updatedAt)),
      h("td", { class: "num" }, h("div", { class: "run-actions" },
        can("Operator") ? h("button", { class: "btn btn-sm btn-primary", disabled: !sp.enabled, onclick: () => runDialog(sp) }, "Run") : null,
        can("Operator") ? h("button", { class: "btn btn-sm", onclick: async () => { await act(sp.enabled ? "Disabled" : "Enabled", () => api(`/spiders/${sp.id}/${sp.enabled ? "disable" : "enable"}`, { method: "POST" })); load(); } }, sp.enabled ? "Disable" : "Enable") : null))))
      : [emptyRow(6, "No spiders yet", can("Admin") ? "Create one with New spider or build one in the Selector builder." : "An admin can add spiders.")]));
  };
  load();
}

function runDialog(sp) {
  const args = h("textarea", { id: "run-args", rows: 4, placeholder: "category=books\nmax_pages=5", spellcheck: "false" });
  args.style.minHeight = "96px";
  const sets = h("textarea", { id: "run-settings", rows: 3, placeholder: "CLOSESPIDER_ITEMCOUNT=100", spellcheck: "false" });
  sets.style.minHeight = "80px";
  openDialog(`Run ${sp.name}`,
    [h("div", { class: "field" }, h("label", { for: "run-args" }, "Spider arguments"), args, h("small", {}, "One key=value per line (the -a options of scrapynet crawl).")),
      h("div", { class: "field" }, h("label", { for: "run-settings" }, "Setting overrides"), sets, h("small", {}, "One KEY=VALUE per line (the -s options)."))],
    "Run now", async () => {
      const job = await act("Job started", () => api(`/spiders/${encodeURIComponent(sp.id)}/run`, { method: "POST", body: { arguments: pairs(args.value), settings: pairs(sets.value) } }));
      if (job) location.hash = `#/job/${encodeURIComponent(job.id)}`;
      return !!job;
    });
}

function pairs(text) {
  const out = {};
  for (const line of text.split(/\r?\n/)) { const i = line.indexOf("="); if (i > 0) out[line.slice(0, i).trim()] = line.slice(i + 1).trim(); }
  return out;
}

const SPIDER_TEMPLATE = (rules, url) => ({
  id: "my-spider",
  name: "my-spider",
  description: "",
  enabled: true,
  target: { startUrls: [url || "https://example.com/"], allowedDomains: [], maxDepth: 0, includePatterns: [], excludePatterns: [] },
  rules: rules ?? { itemSelector: "div.item", fields: { title: { css: "h2::text", processors: ["clean"] } }, followLinks: ["li.next a"], detailLinks: [], requiredFields: ["title"] },
  settings: { DOWNLOAD_DELAY: 0.5 },
  tags: [],
});

async function spiderEditor(main, id) {
  const isNew = !id || id === "new";
  const draft = isNew ? (spiderEditor.draft ?? SPIDER_TEMPLATE()) : await act(null, () => api(`/spiders/${encodeURIComponent(id)}`));
  spiderEditor.draft = null;
  if (!draft) { main.append(h("a", { class: "back", href: "#/spiders" }, "← Spiders"), h("p", {}, "This spider no longer exists.")); return; }
  const editor = h("textarea", { id: "spider-json", spellcheck: "false", "aria-describedby": "spider-help", readonly: !can("Admin") });
  const { createdAt, updatedAt, isDeclarative, version, ...editable } = draft;
  editor.value = JSON.stringify(editable, null, 2);
  const error = h("p", { class: "form-error", role: "alert" });
  const versions = h("tbody");

  main.append(h("a", { class: "back", href: "#/spiders" }, "← Spiders"),
    pageHead(isNew ? "New spider" : draft.name, isNew ? "A declarative spider: target, extraction rules and settings, as JSON." : `Version ${version} · ${draft.spiderType ? "code spider" : "declarative"}`,
      !isNew && can("Operator") ? h("button", { class: "btn btn-primary", disabled: !draft.enabled, onclick: () => runDialog(draft) }, "Run") : null,
      !isNew && can("Admin") ? h("button", { class: "btn", onclick: () => cloneDialog(draft) }, "Clone") : null,
      !isNew && can("Admin") ? h("button", { class: "btn btn-danger", onclick: () => deleteSpider(draft) }, "Delete") : null),
    h("div", { class: "grid grid-dash" },
      h("section", { class: "panel" },
        h("div", { class: "field" }, h("label", { for: "spider-json" }, "Definition"), editor,
          h("small", { id: "spider-help" }, "Fields: target (startUrls, allowedDomains, maxDepth, includePatterns, excludePatterns), rules (itemSelector, fields, followLinks, detailLinks, requiredFields), settings (values may use ${secret:name}), tags.")),
        error,
        can("Admin") ? h("div", { class: "toolbar" }, h("button", { class: "btn btn-primary", onclick: save }, isNew ? "Create spider" : "Save new version")) : null),
      isNew ? h("section", { class: "panel" }, h("h2", {}, "Tips"),
        h("p", {}, "Try selectors on the live page first in the ", h("a", { href: "#/builder" }, "Selector builder"), ", then save from there."),
        h("p", { class: "muted" }, "Processors: strip, clean, lower, upper, remove_tags, price, int, decimal, absolute_url, date."))
        : h("section", { class: "panel" }, h("div", { class: "panel-head" }, h("h2", {}, "Versions")),
          h("div", { class: "table-wrap" }, h("table", {}, h("thead", {}, h("tr", {}, h("th", {}, "Version"), h("th", {}, "Saved"), h("th", {}, ""))), versions)))));

  if (!isNew) {
    const list = await act(null, () => api(`/spiders/${encodeURIComponent(id)}/versions`));
    versions.replaceChildren(...(list ?? []).slice().reverse().map((v) => h("tr", {},
      h("td", {}, `v${v.version}`, v.version === version ? h("span", { class: "cell-sub" }, " current") : null),
      h("td", {}, time(v.updatedAt)),
      h("td", { class: "num" }, can("Admin") && v.version !== version ? h("button", { class: "btn btn-sm", onclick: async () => { if (await act(`Rolled back to v${v.version}`, () => api(`/spiders/${encodeURIComponent(id)}/rollback/${v.version}`, { method: "POST" }))) route(); } }, "Roll back") : null))));
  }

  async function save() {
    let definition;
    try { definition = JSON.parse(editor.value); } catch (e) { error.textContent = `The definition is not valid JSON: ${e.message}`; return; }
    error.textContent = "";
    const saved = await act(isNew ? "Spider created" : "New version saved", () => isNew
      ? api("/spiders", { method: "POST", body: definition })
      : api(`/spiders/${encodeURIComponent(id)}`, { method: "PUT", body: { ...definition, id } }));
    if (saved) location.hash = `#/spider/${encodeURIComponent(saved.id)}`;
    if (saved && !isNew) route();
  }
}

function cloneDialog(sp) {
  const newId = h("input", { id: "clone-id", value: `${sp.id}-copy`, spellcheck: "false" });
  const newName = h("input", { id: "clone-name", value: `${sp.name}-copy` });
  openDialog(`Clone ${sp.name}`, [h("div", { class: "field" }, h("label", { for: "clone-id" }, "New id"), newId), h("div", { class: "field" }, h("label", { for: "clone-name" }, "New name"), newName)],
    "Clone", async () => {
      const copy = await act("Spider cloned", () => api(`/spiders/${encodeURIComponent(sp.id)}/clone`, { method: "POST", body: { newId: newId.value, newName: newName.value } }));
      if (copy) location.hash = `#/spider/${encodeURIComponent(copy.id)}`;
      return !!copy;
    });
}

function deleteSpider(sp) {
  openDialog(`Delete ${sp.name}?`, [h("p", {}, "The spider and its version history are removed. Job history and scraped data stay.")], "Delete spider", async () => {
    const ok = await act("Spider deleted", () => api(`/spiders/${encodeURIComponent(sp.id)}`, { method: "DELETE" }));
    if (ok !== undefined) location.hash = "#/spiders";
    return ok !== undefined;
  }, true);
}

// ---- jobs ---------------------------------------------------------------------------------------
async function jobs(main) {
  const filter = h("select", { id: "job-filter", "aria-label": "Spider" }, h("option", { value: "" }, "All spiders"));
  const body = h("tbody");
  main.append(pageHead("Jobs", "Every run, newest first."),
    h("section", { class: "panel" },
      h("div", { class: "toolbar" }, filter),
      h("div", { class: "table-wrap" }, h("table", {},
        h("thead", {}, h("tr", {}, h("th", {}, "Job"), h("th", {}, "Status"), h("th", {}, "Trigger"), h("th", {}, "Started"), h("th", {}, "Duration"),
          h("th", { class: "num" }, "Items"), h("th", { class: "num" }, "Pages"), h("th", { class: "num" }, "Errors"))),
        body))));
  const spidersList = await act(null, () => api("/spiders")) ?? [];
  for (const sp of spidersList) filter.append(h("option", { value: sp.id }, sp.name));
  filter.addEventListener("change", () => refresh());
  const refresh = async () => {
    const list = await api(`/jobs?limit=200${filter.value ? `&spider=${encodeURIComponent(filter.value)}` : ""}`);
    body.replaceChildren(...(list.length ? list.map((j) => h("tr", { class: "clickable", tabindex: 0, onclick: () => (location.hash = `#/job/${encodeURIComponent(j.id)}`), onkeydown: (e) => { if (e.key === "Enter") location.hash = `#/job/${encodeURIComponent(j.id)}`; } },
      h("td", {}, h("div", { class: "cell-main" }, j.spiderName), h("div", { class: "cell-sub mono" }, j.id)),
      h("td", {}, badge(j.status)), h("td", {}, j.trigger), h("td", {}, time(j.startedAt ?? j.createdAt)), h("td", {}, duration(j.startedAt, j.finishedAt)),
      h("td", { class: "num" }, num(j.itemCount)), h("td", { class: "num" }, num(j.responseCount)), h("td", { class: "num" }, num(j.errorCount))))
      : [emptyRow(8, "No jobs yet", "Run a spider and its job shows up here.")]));
  };
  poll(refresh);
}

async function jobDetail(main, id) {
  const head = h("div");
  const facts = h("dl", { class: "kv" });
  const chart = h("div");
  const items = h("div", { class: "table-wrap" });
  main.append(h("a", { class: "back", href: "#/jobs" }, "← Jobs"), head,
    h("div", { class: "grid grid-dash" },
      h("section", { class: "panel" }, h("div", { class: "panel-head" }, h("h2", {}, "Throughput")), chart),
      h("section", { class: "panel" }, h("div", { class: "panel-head" }, h("h2", {}, "Run")), facts)),
    h("section", { class: "panel" }, h("div", { class: "panel-head" }, h("h2", {}, "Items"), h("p", {}, "First 50 items of this run")), items));
  main.lastElementChild.style.marginTop = "16px";
  let itemsLoaded = false;

  poll(async () => {
    const job = await api(`/jobs/${encodeURIComponent(id)}`);
    const active = ["Running", "Paused", "Pending"].includes(job.status);
    head.replaceChildren(pageHead(job.spiderName, `Job ${job.id}`,
      ...(can("Operator") ? [
        job.status === "Running" ? h("button", { class: "btn", onclick: () => act("Paused", () => api(`/jobs/${id}/pause`, { method: "POST" })) }, "Pause") : null,
        job.status === "Paused" ? h("button", { class: "btn", onclick: () => act("Resumed", () => api(`/jobs/${id}/resume`, { method: "POST" })) }, "Resume") : null,
        active ? h("button", { class: "btn", onclick: () => act("Stopping — the queue is kept for a resume", () => api(`/jobs/${id}/stop`, { method: "POST" })) }, "Stop") : null,
        active ? h("button", { class: "btn btn-danger", onclick: () => act("Cancelled", () => api(`/jobs/${id}/cancel`, { method: "POST" })) }, "Cancel") : null,
        !active ? h("button", { class: "btn btn-primary", onclick: async () => { const r = await act("Retry started", () => api(`/jobs/${id}/retry`, { method: "POST" })); if (r) location.hash = `#/job/${encodeURIComponent(r.id)}`; } }, "Retry") : null,
      ] : [])));
    facts.replaceChildren(...[
      ["Status", badge(job.status)], ["Trigger", job.trigger], ["Attempt", job.attempt], ["Spider version", `v${job.spiderVersion}`],
      ["Started", time(job.startedAt)], ["Finished", time(job.finishedAt)], ["Duration", duration(job.startedAt, job.finishedAt)],
      ["Finish reason", job.finishReason ?? "—"], ["Items", num(job.itemCount)], ["Requests", num(job.requestCount)],
      ["Responses", num(job.responseCount)], ["Errors", num(job.errorCount)], job.error ? ["Error", job.error] : null,
    ].filter(Boolean).flatMap(([k, v]) => [h("dt", {}, k), h("dd", {}, v)]));
    const metrics = await api(`/jobs/${encodeURIComponent(id)}/metrics`).catch(() => null);
    chart.replaceChildren(...[metrics ? throughputChart(metrics.series, chart.clientWidth, !active) : h("p", { class: "muted" }, "No live metrics for this run (it ran before the server started)."),
      metrics ? h("div", { class: "panel-head" }, h("h3", {}, "Responses by status")) : null, metrics ? statusBar(metrics.statusCounts) : null].filter(Boolean));
    if (!itemsLoaded || active) {
      const rows = await api(`/jobs/${encodeURIComponent(id)}/items?limit=50`);
      items.replaceChildren(itemsTable(rows));
      itemsLoaded = true;
    }
  });
}

function itemsTable(rows) {
  if (!rows?.length) return h("p", { class: "empty" }, h("strong", {}, "No items yet"), "Items appear as the spider yields them.");
  const keys = [...new Set(rows.flatMap((r) => Object.keys(r)))].slice(0, 8);
  const show = (v) => v === null || v === undefined ? "" : typeof v === "object" ? JSON.stringify(v) : String(v);
  return h("table", { class: "items-table" }, h("thead", {}, h("tr", {}, keys.map((k) => h("th", {}, k)))),
    h("tbody", {}, rows.map((r) => h("tr", {}, keys.map((k) => h("td", {}, show(r[k]).slice(0, 400)))))));
}

// ---- schedules ----------------------------------------------------------------------------------
const PRESETS = [["Every 15 minutes", "*/15 * * * *"], ["Hourly", "@hourly"], ["Daily at 02:00", "0 2 * * *"], ["Weekly, Monday 06:00", "0 6 * * 1"], ["Monthly, 1st at 03:00", "0 3 1 * *"]];

async function schedules(main) {
  const body = h("tbody");
  main.append(pageHead("Schedules", "Cron schedules that start jobs.", can("Operator") ? h("button", { class: "btn btn-primary", onclick: () => scheduleDialog(load) }, "New schedule") : null),
    h("section", { class: "panel" }, h("div", { class: "table-wrap" }, h("table", {},
      h("thead", {}, h("tr", {}, h("th", {}, "Schedule"), h("th", {}, "Spider"), h("th", {}, "Cron"), h("th", {}, "Next run"), h("th", {}, "Last run"), h("th", {}, "State"), h("th", {}, ""))),
      body))));
  const load = async () => {
    const list = await act(null, () => api("/schedules"));
    if (!list) return;
    body.replaceChildren(...(list.length ? list.map((e) => h("tr", {},
      h("td", { class: "cell-main mono" }, e.id), h("td", {}, e.spiderId), h("td", { class: "mono" }, e.cron, e.timeZone ? h("div", { class: "cell-sub" }, e.timeZone) : null),
      h("td", {}, e.enabled ? time(e.nextRun) : "—"),
      h("td", {}, e.lastJobId ? h("a", { href: `#/job/${encodeURIComponent(e.lastJobId)}` }, ago(e.lastRun)) : "never"),
      h("td", {}, h("span", { class: `badge ${e.enabled ? "ok" : "off"}` }, e.enabled ? "Active" : "Paused")),
      h("td", { class: "num" }, can("Operator") ? h("div", { class: "run-actions" },
        h("button", { class: "btn btn-sm", onclick: async () => { await act(e.enabled ? "Schedule paused" : "Schedule active", () => api(`/schedules/${encodeURIComponent(e.id)}/${e.enabled ? "disable" : "enable"}`, { method: "POST" })); load(); } }, e.enabled ? "Pause" : "Activate"),
        h("button", { class: "btn btn-sm btn-danger", onclick: async () => { if (await act("Schedule deleted", () => api(`/schedules/${encodeURIComponent(e.id)}`, { method: "DELETE" })) !== undefined) load(); } }, "Delete")) : null)))
      : [emptyRow(7, "No schedules", can("Operator") ? "Add one to run a spider every N minutes, hourly, daily, weekly or monthly." : "An operator can add schedules.")]));
  };
  load();
}

async function scheduleDialog(done) {
  const list = await act(null, () => api("/spiders")) ?? [];
  const id = h("input", { id: "sch-id", spellcheck: "false", required: true });
  const spider = h("select", { id: "sch-spider" }, list.map((sp) => h("option", { value: sp.id }, sp.name)));
  const cron = h("input", { id: "sch-cron", class: "mono", value: "@hourly", spellcheck: "false" });
  const preset = h("select", { id: "sch-preset", "aria-label": "Preset", onchange: () => { if (preset.value) cron.value = preset.value; } },
    h("option", { value: "" }, "Presets…"), PRESETS.map(([label, value]) => h("option", { value }, label)));
  const tz = h("input", { id: "sch-tz", placeholder: "UTC", spellcheck: "false" });
  const overlap = h("input", { type: "checkbox", id: "sch-overlap" });
  spider.addEventListener("change", () => { if (!id.dataset.touched) id.value = `${spider.value}-schedule`; });
  id.addEventListener("input", () => (id.dataset.touched = "1"));
  id.value = list[0] ? `${list[0].id}-schedule` : "";
  openDialog("New schedule", [h("div", { class: "form-grid" },
    h("div", { class: "field" }, h("label", { for: "sch-spider" }, "Spider"), spider),
    h("div", { class: "field" }, h("label", { for: "sch-id" }, "Schedule id"), id),
    h("div", { class: "field" }, h("label", { for: "sch-cron" }, "Cron expression"), cron, h("small", {}, "minute hour day month weekday, or @hourly, @daily, @weekly, @monthly")),
    h("div", { class: "field" }, h("label", { for: "sch-preset" }, "Preset"), preset),
    h("div", { class: "field" }, h("label", { for: "sch-tz" }, "Time zone"), tz, h("small", {}, "IANA or Windows id; empty means UTC")),
    h("label", { class: "check wide" }, overlap, "Start even if the previous run of this spider is still going"))],
    "Create schedule", async () => {
      const ok = await act("Schedule created", () => api("/schedules", { method: "POST", body: { id: id.value, spiderId: spider.value, cron: cron.value, timeZone: tz.value || null, allowOverlap: overlap.checked } }));
      if (ok) done();
      return !!ok;
    });
}

// ---- selector builder ---------------------------------------------------------------------------
function builder(main) {
  const url = h("input", { id: "b-url", type: "url", placeholder: "https://quotes.toscrape.com/", spellcheck: "false" });
  const itemSel = h("input", { id: "b-item", class: "mono", placeholder: "div.quote", spellcheck: "false" });
  const follow = h("input", { id: "b-follow", class: "mono", placeholder: "li.next a", spellcheck: "false" });
  const rows = h("div", { class: "rule-rows" });
  const result = h("div", { class: "table-wrap" }, h("p", { class: "muted" }, "Results appear here."));
  const status = h("p", { class: "muted" });

  const addRow = (name = "", selector = "", kind = "css", regex = "", processors = "", multiple = false) => {
    const row = h("div", { class: "rule-row" },
      h("input", { "aria-label": "Field name", value: name, placeholder: "title", spellcheck: "false" }),
      h("input", { "aria-label": "Selector", value: selector, placeholder: "span.text::text", spellcheck: "false" }),
      h("select", { "aria-label": "Selector kind" }, h("option", { value: "css", selected: kind === "css" }, "CSS"), h("option", { value: "xpath", selected: kind === "xpath" }, "XPath")),
      h("input", { "aria-label": "Processors", value: processors, placeholder: "clean", spellcheck: "false" }),
      h("label", { class: "check" }, h("input", { type: "checkbox", "aria-label": "All matches", checked: multiple }), "all"),
      h("button", { class: "btn btn-quiet", "aria-label": "Remove field", onclick: () => row.remove() }, "✕"));
    row.dataset.regex = regex;
    rows.append(row);
  };
  addRow("text", "span.text::text", "css", "", "clean");
  addRow("author", "small.author::text", "css", "", "clean");
  addRow("tags", "a.tag::text", "css", "", "", true);

  const rules = () => {
    const fields = {};
    for (const row of rows.children) {
      const [name, sel, kind, procs, check] = row.querySelectorAll("input, select");
      if (!name.value.trim() || !sel.value.trim()) continue;
      fields[name.value.trim()] = {
        [kind.value]: sel.value.trim(),
        processors: procs.value.split(",").map((p) => p.trim()).filter(Boolean),
        multiple: check.checked,
      };
    }
    return { itemSelector: itemSel.value.trim() || null, fields, followLinks: follow.value.trim() ? [follow.value.trim()] : [], detailLinks: [], requiredFields: [] };
  };

  main.append(pageHead("Selector builder", "Try extraction rules on a live page, then save them as a spider."),
    h("section", { class: "panel" },
      h("div", { class: "form-grid" },
        h("div", { class: "field wide" }, h("label", { for: "b-url" }, "Page URL"), url),
        h("div", { class: "field" }, h("label", { for: "b-item" }, "Item container"), itemSel, h("small", {}, "CSS selector repeated once per item; empty = one item per page")),
        h("div", { class: "field" }, h("label", { for: "b-follow" }, "Follow links"), follow, h("small", {}, "Pagination links crawled with the same rules"))),
      h("h3", { class: "rule-title" }, "Fields"),
      h("div", { class: "rule-head", "aria-hidden": "true" }, h("span", {}, "Name"), h("span", {}, "Selector"), h("span", {}, "Kind"), h("span", {}, "Processors"), h("span", {}, ""), h("span", {}, "")),
      rows,
      h("div", { class: "toolbar" },
        h("button", { class: "btn", onclick: () => addRow() }, "Add field"),
        h("button", { class: "btn btn-primary", disabled: !can("Operator"), onclick: preview }, "Preview"),
        can("Admin") ? h("button", { class: "btn", onclick: () => { spiderEditor.draft = { ...SPIDER_TEMPLATE(rules(), url.value), id: "", name: "" }; location.hash = "#/spider/new"; } }, "Save as spider") : null,
        status)),
    h("section", { class: "panel" }, h("div", { class: "panel-head" }, h("h2", {}, "Extracted items")), result));
  main.querySelector(".rule-title").style.margin = "18px 0 8px";
  main.lastElementChild.style.marginTop = "16px";

  async function preview() {
    if (!url.value) { status.textContent = "Enter the URL of a page to try the rules on."; url.focus(); return; }
    status.textContent = "Fetching…";
    const r = await act(null, () => api("/preview", { method: "POST", body: { url: url.value, rules: rules() } }));
    if (!r) { status.textContent = ""; return; }
    status.textContent = `HTTP ${r.status} · ${r.items.length} item${r.items.length === 1 ? "" : "s"}`;
    result.replaceChildren(itemsTable(r.items));
  }
}

// ---- alerts -------------------------------------------------------------------------------------
async function alerts(main) {
  const body = h("tbody");
  main.append(pageHead("Alerts", "Failures, empty runs, sharp drops in items, expired logins.",
    can("Admin") ? h("button", { class: "btn btn-primary", onclick: webhookDialog }, "Send alerts to…") : null),
    h("section", { class: "panel" }, h("div", { class: "table-wrap" }, h("table", {},
      h("thead", {}, h("tr", {}, h("th", {}, "When"), h("th", {}, "Severity"), h("th", {}, "Alert"), h("th", {}, "Spider"), h("th", {}, "Message"))), body))));
  poll(async () => {
    const list = await api("/alerts");
    body.replaceChildren(...(list.length ? list.slice(0, 200).map((a) => {
      const [cls, label] = SEVERITY[a.severity] ?? ["off", a.severity];
      return h("tr", {},
        h("td", {}, h("div", {}, ago(a.time)), h("div", { class: "cell-sub" }, time(a.time))),
        h("td", {}, h("span", { class: `badge ${cls}` }, label)), h("td", {}, words(a.type)),
        h("td", {}, a.spiderId), h("td", {}, a.jobId ? h("a", { href: `#/job/${encodeURIComponent(a.jobId)}` }, a.message) : a.message));
    }) : [emptyRow(5, "No alerts", "Alerts appear when a job fails, finds nothing, or yields far fewer items than usual.")]));
  });
}

function webhookDialog() {
  const url = h("input", { id: "wh-url", type: "url", placeholder: "https://hooks.slack.com/services/…", spellcheck: "false" });
  const kind = h("select", { id: "wh-kind" }, h("option", { value: "slack" }, "Slack"), h("option", { value: "teams" }, "Microsoft Teams"), h("option", { value: "webhook" }, "Webhook (JSON POST)"));
  const min = h("select", { id: "wh-min" }, h("option", { value: "Info" }, "Info and above"), h("option", { value: "Warning", selected: true }, "Warning and above"), h("option", { value: "Critical" }, "Critical only"));
  openDialog("Send alerts to a channel", [h("div", { class: "form-grid" },
    h("div", { class: "field wide" }, h("label", { for: "wh-url" }, "Incoming webhook URL"), url),
    h("div", { class: "field" }, h("label", { for: "wh-kind" }, "Channel"), kind),
    h("div", { class: "field" }, h("label", { for: "wh-min" }, "Send"), min))],
    "Add channel", async () => !!(await act("Channel added", () => api("/webhooks", { method: "POST", body: { url: url.value, kind: kind.value, minSeverity: min.value } }))));
}

// ---- dialog -------------------------------------------------------------------------------------
function openDialog(title, content, confirmLabel, onConfirm, danger = false) {
  const dialog = $("#dialog");
  const confirm = h("button", { class: `btn ${danger ? "btn-danger" : "btn-primary"}` }, confirmLabel);
  confirm.addEventListener("click", async () => { confirm.disabled = true; const ok = await onConfirm(); confirm.disabled = false; if (ok) dialog.close(); });
  dialog.replaceChildren(
    h("div", { class: "dialog-body" }, h("h2", {}, title), content),
    h("div", { class: "dialog-foot" }, h("button", { class: "btn", onclick: () => dialog.close() }, "Cancel"), confirm));
  dialog.showModal();
}

// ---- start --------------------------------------------------------------------------------------
function applyTheme() {
  const theme = store.get("scrapynet.theme");
  if (theme === "light" || theme === "dark") document.documentElement.dataset.theme = theme;
  else delete document.documentElement.dataset.theme;
  const button = $("#theme");
  if (button) button.textContent = `Theme: ${theme === "light" ? "Light" : theme === "dark" ? "Dark" : "Auto"}`;
}

function cycleTheme() {
  const order = [null, "light", "dark"];
  const next = order[(order.indexOf(store.get("scrapynet.theme")) + 1) % order.length];
  if (next) store.set("scrapynet.theme", next, true); else store.clear("scrapynet.theme");
  applyTheme();
}

applyTheme();
$("#signin-form").addEventListener("submit", async (e) => {
  e.preventDefault();
  $("#signin-error").textContent = "";
  try { await signIn($("#api-key").value.trim(), $("#remember").checked); }
  catch (err) { if (err.message !== "Unauthorized") $("#signin-error").textContent = err.message; }
});
$("#signout").addEventListener("click", () => signOut());
$("#theme").addEventListener("click", cycleTheme);
$("#nav-toggle").addEventListener("click", () => {
  const open = $("#rail").classList.toggle("open");
  $("#nav-toggle").setAttribute("aria-expanded", String(open));
});
window.addEventListener("hashchange", route);

const saved = store.get("scrapynet.key");
if (saved) signIn(saved, !!(() => { try { return localStorage.getItem("scrapynet.key"); } catch { return null; } })()).catch(() => signOut());
else signOut();

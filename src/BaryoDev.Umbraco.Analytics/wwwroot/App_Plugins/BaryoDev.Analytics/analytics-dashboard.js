// Plain custom element with no build step, same approach as the Medium import dashboard.
const API = "/umbraco/management/api/v1/baryo-analytics";
const BASE = "/App_Plugins/BaryoDev.Analytics";
const RANGES = [7, 30, 90];
const DAY = "24h";
const V = new URL(import.meta.url).search;
const LOGO = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 134 32" role="img" aria-label="baryo.dev"><rect x="0" y="0" width="32" height="32" rx="8" fill="#0b7a55"/><path d="M9 8v16h7a5 5 0 0 0 0-10h-3" fill="none" stroke="#fbf7ee" stroke-width="3.2" stroke-linecap="round" stroke-linejoin="round"/><circle cx="24" cy="9" r="2.4" fill="#ffcd3c"/><text x="40" y="22" font-family="'Plus Jakarta Sans', system-ui, sans-serif" font-size="18" font-weight="700" fill="currentColor">baryo.dev</text></svg>`;

const Base = await import("@umbraco-cms/backoffice/element-api")
  .then(({ UmbElementMixin }) => UmbElementMixin(HTMLElement))
  .catch(() => HTMLElement);

function esc(value) {
  return String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
}

const tokens = new WeakMap();

async function token(host) {
  if (!tokens.has(host)) {
    tokens.set(host, Promise.all([
      import("@umbraco-cms/backoffice/context-api"),
      import("@umbraco-cms/backoffice/auth"),
    ]).then(([{ UmbContextConsumer }, { UMB_AUTH_CONTEXT }]) => {
      const consumer = new UmbContextConsumer(host, UMB_AUTH_CONTEXT);
      const auth = consumer.asPromise();
      consumer.request();
      return auth;
    }).catch(() => null));
  }
  const auth = await tokens.get(host);
  return auth ? auth.getLatestToken() : null;
}

async function load(host, range) {
  const t = await token(host);
  const url = range === DAY ? `${API}/last-24-hours` : `${API}/summary?days=${range}`;
  const response = await fetch(url, {
    headers: t ? { Authorization: `Bearer ${t}` } : {},
    credentials: "same-origin",
  });
  if (!response.ok) throw new Error(`The numbers could not be loaded (${response.status}).`);
  return response.json();
}

// Hours arrive as UTC instants. They are shown in the backoffice user's local time.
function hourLabel(iso) {
  return new Date(iso).toLocaleTimeString([], { hour: "numeric" });
}

function hourPoints(hourly) {
  return hourly.map((h) => ({
    label: hourLabel(h.hour),
    title: `${new Date(h.hour).toLocaleString([], { dateStyle: "medium", timeStyle: "short" })}`,
    pageViews: h.pageViews,
    visitors: h.visitors,
  }));
}

function dayPoints(daily) {
  return daily.map((d) => ({ label: d.day.slice(5), title: d.day, pageViews: d.pageViews, visitors: d.visitors }));
}

function chart(daily, what) {
  const W = 720, H = 200, left = 36, bottom = 22, top = 8;
  const max = Math.max(1, ...daily.map((d) => d.pageViews));
  const inner = W - left;
  const slot = inner / daily.length;
  const barW = Math.max(1, slot * 0.7);
  const plot = H - bottom - top;
  const bars = daily.map((d, i) => {
    const h = Math.round((d.pageViews / max) * plot);
    const x = left + i * slot + (slot - barW) / 2;
    return `<rect class="bar" x="${x.toFixed(1)}" y="${top + plot - h}" width="${barW.toFixed(1)}" height="${h}" rx="2"><title>${esc(d.title)}: ${esc(d.pageViews)} page views, ${esc(d.visitors)} visitors</title></rect>`;
  }).join("");
  const step = Math.ceil(daily.length / 6);
  const labels = daily.map((d, i) => (i % step === 0
    ? `<text x="${(left + i * slot + slot / 2).toFixed(1)}" y="${H - 6}" text-anchor="middle">${esc(d.label)}</text>` : "")).join("");
  return `<svg viewBox="0 0 ${W} ${H}" role="img" aria-label="Page views per ${what}">
    <line class="axis" x1="${left}" y1="${top + plot}" x2="${W}" y2="${top + plot}"/>
    <line class="axis" x1="${left}" y1="${top}" x2="${W}" y2="${top}"/>
    <text x="${left - 6}" y="${top + 4}" text-anchor="end">${esc(max)}</text>
    <text x="${left - 6}" y="${top + plot}" text-anchor="end">0</text>
    ${bars}${labels}</svg>`;
}

function table(title, rows, label, empty) {
  const body = rows.length
    ? rows.map((r) => `<uui-table-row><uui-table-cell class="ba-v">${esc(r.value || empty)}</uui-table-cell><uui-table-cell class="ba-n">${esc(r.visitors)}</uui-table-cell><uui-table-cell class="ba-n">${esc(r.pageViews)}</uui-table-cell></uui-table-row>`).join("")
    : `<uui-table-row><uui-table-cell><span class="ba-note">No data yet.</span></uui-table-cell><uui-table-cell></uui-table-cell><uui-table-cell></uui-table-cell></uui-table-row>`;
  return `<uui-box headline="${esc(title)}"><uui-table><uui-table-head><uui-table-head-cell>${esc(label)}</uui-table-head-cell><uui-table-head-cell class="ba-n">Visitors</uui-table-head-cell><uui-table-head-cell class="ba-n">Views</uui-table-head-cell></uui-table-head>${body}</uui-table></uui-box>`;
}

function stat(title, value) {
  return `<uui-box headline="${esc(title)}"><div class="ba-num">${esc(value)}</div></uui-box>`;
}

function cards(t) {
  return `<div class="ba-cards">${stat("Visitors", t.visitors)}${stat("Visits", t.visits)}${stat("Page views", t.pageViews)}</div>`;
}

class BaryoAnalyticsDashboard extends Base {
  constructor() {
    super();
    this.range = 30;
    this.root = this.attachShadow({ mode: "open" });
    this.root.innerHTML = `<link rel="stylesheet" href="${BASE}/dashboard.css${V}"><div class="ba" id="app"></div>`;
    this.root.addEventListener("click", (e) => {
      const b = e.target.closest("[data-range]");
      if (b) { this.range = b.dataset.range === DAY ? DAY : Number(b.dataset.range); this.refresh(); }
    });
  }

  connectedCallback() {
    super.connectedCallback?.();
    this.refresh();
  }

  async refresh() {
    const app = this.root.getElementById("app");
    const head = `<div class="ba-head">${LOGO}<h1 class="ba-title">Analytics</h1></div>
      <uui-tab-group>${[DAY, ...RANGES].map((d) => {
    const text = d === DAY ? "Last 24 hours" : `${d} days`;
    return `<uui-tab data-range="${d}" label="${text}" ${d === this.range ? "active" : ""}><span>${text}</span></uui-tab>`;
  }).join("")}</uui-tab-group>`;
    try {
      const s = await load(this, this.range);
      if (this.range === DAY) {
        app.innerHTML = `${head}
        ${cards(s.totals)}
        <uui-box class="ba-chart" headline="Page views per hour">${chart(hourPoints(s.hourly), "hour")}</uui-box>
        <div class="ba-grid">
          ${table("Top pages", s.topPages, "Page", "/")}
          ${table("Top referrers", s.topReferrers, "Referrer", "Direct")}
        </div>
        <p class="ba-note">Hours are shown in your local time. The last 24 hours read the raw events, so a visitor is counted once per range.</p>`;
        return;
      }
      app.innerHTML = `${head}
        ${cards(s.totals)}
        <uui-box class="ba-chart" headline="Page views per day">${chart(dayPoints(s.daily), "day")}</uui-box>
        <div class="ba-grid">
          ${table("Top pages", s.topPages, "Page", "/")}
          ${table("Top referrers", s.topReferrers, "Referrer", "Direct")}
          ${table("Devices", s.devices, "Device", "Unknown")}
          ${table("Browsers", s.browsers, "Browser", "Unknown")}
          ${s.countries.length ? table("Countries", s.countries, "Country", "Unknown") : ""}
        </div>
        <p class="ba-note">Counts use a salted hash, no cookies. Raw IP addresses and User-Agents are never stored.</p>`;
    } catch (e) {
      app.innerHTML = `${head}<div class="ba-err">${esc(e.message)}</div>`;
    }
  }
}

customElements.define("baryo-analytics-dashboard", BaryoAnalyticsDashboard);
export default BaryoAnalyticsDashboard;
export { BaryoAnalyticsDashboard as element };

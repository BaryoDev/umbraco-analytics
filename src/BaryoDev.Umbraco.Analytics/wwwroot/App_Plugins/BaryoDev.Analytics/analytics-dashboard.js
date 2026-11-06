// Plain custom element with no build step, same approach as the Medium import dashboard.
const API = "/umbraco/management/api/v1/baryo-analytics";
const BASE = "/App_Plugins/BaryoDev.Analytics";
const RANGES = [7, 30, 90];

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

async function load(host, days) {
  const t = await token(host);
  const response = await fetch(`${API}/summary?days=${days}`, {
    headers: t ? { Authorization: `Bearer ${t}` } : {},
    credentials: "same-origin",
  });
  if (!response.ok) throw new Error(`The numbers could not be loaded (${response.status}).`);
  return response.json();
}

function chart(daily) {
  const W = 720, H = 200, left = 36, bottom = 22, top = 8;
  const max = Math.max(1, ...daily.map((d) => d.pageViews));
  const inner = W - left;
  const slot = inner / daily.length;
  const barW = Math.max(1, slot * 0.7);
  const plot = H - bottom - top;
  const bars = daily.map((d, i) => {
    const h = Math.round((d.pageViews / max) * plot);
    const x = left + i * slot + (slot - barW) / 2;
    return `<rect class="bar" x="${x.toFixed(1)}" y="${top + plot - h}" width="${barW.toFixed(1)}" height="${h}" rx="2"><title>${esc(d.day)}: ${d.pageViews} page views, ${d.visitors} visitors</title></rect>`;
  }).join("");
  const step = Math.ceil(daily.length / 6);
  const labels = daily.map((d, i) => (i % step === 0
    ? `<text x="${(left + i * slot + slot / 2).toFixed(1)}" y="${H - 6}" text-anchor="middle">${esc(d.day.slice(5))}</text>` : "")).join("");
  return `<svg viewBox="0 0 ${W} ${H}" role="img" aria-label="Page views per day">
    <line class="axis" x1="${left}" y1="${top + plot}" x2="${W}" y2="${top + plot}"/>
    <line class="axis" x1="${left}" y1="${top}" x2="${W}" y2="${top}"/>
    <text x="${left - 6}" y="${top + 4}" text-anchor="end">${max}</text>
    <text x="${left - 6}" y="${top + plot}" text-anchor="end">0</text>
    ${bars}${labels}</svg>`;
}

function table(title, rows, label, empty) {
  const body = rows.length
    ? rows.map((r) => `<tr><td class="v">${esc(r.value || empty)}</td><td class="n">${r.visitors}</td><td class="n">${r.pageViews}</td></tr>`).join("")
    : `<tr><td colspan="3" class="ba-note">No data yet.</td></tr>`;
  return `<div class="ba-card"><h3>${esc(title)}</h3><table class="ba-table"><thead><tr><th>${esc(label)}</th><th class="n">Visitors</th><th class="n">Views</th></tr></thead><tbody>${body}</tbody></table></div>`;
}

class BaryoAnalyticsDashboard extends Base {
  constructor() {
    super();
    this.days = 30;
    this.root = this.attachShadow({ mode: "open" });
    this.root.innerHTML = `<link rel="stylesheet" href="${BASE}/brand.css"><div class="ba" id="app"></div>`;
    this.root.addEventListener("click", (e) => {
      const b = e.target.closest("[data-days]");
      if (b) { this.days = Number(b.dataset.days); this.refresh(); }
    });
  }

  connectedCallback() {
    super.connectedCallback?.();
    this.refresh();
  }

  async refresh() {
    const app = this.root.getElementById("app");
    const head = `<div class="ba-head"><h2>Analytics</h2><div class="ba-range">${RANGES.map((d) =>
      `<button class="ba-btn" data-days="${d}" aria-pressed="${d === this.days}">${d} days</button>`).join("")}</div></div>`;
    try {
      const s = await load(this, this.days);
      app.innerHTML = `${head}
        <div class="ba-cards">
          <div class="ba-card"><h3>Visitors</h3><div class="ba-num">${s.totals.visitors}</div></div>
          <div class="ba-card"><h3>Visits</h3><div class="ba-num">${s.totals.visits}</div></div>
          <div class="ba-card"><h3>Page views</h3><div class="ba-num">${s.totals.pageViews}</div></div>
        </div>
        <div class="ba-card ba-chart"><h3>Page views per day</h3>${chart(s.daily)}</div>
        <div class="ba-grid">
          ${table("Top pages", s.topPages, "Page", "/")}
          ${table("Top referrers", s.topReferrers, "Referrer", "Direct")}
          ${table("Devices", s.devices, "Device", "Unknown")}
          ${table("Browsers", s.browsers, "Browser", "Unknown")}
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

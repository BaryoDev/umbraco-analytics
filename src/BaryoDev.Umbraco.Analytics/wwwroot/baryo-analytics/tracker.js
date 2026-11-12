// BaryoDev.Umbraco.Analytics tracker. This script sets no cookies and uses no storage. Sends the path, referrer and query string.
// In Cookie salt mode the server sets a first-party id cookie on the beacon response.
(function () {
  var script = document.currentScript;
  var endpoint = (script && script.getAttribute("data-endpoint")) ||
    (script && script.src ? new URL("collect", script.src).href : "/baryo-analytics/collect");
  var last = null;

  function send(referrer) {
    var body = JSON.stringify({ p: location.pathname, r: referrer, q: location.search });
    if (navigator.sendBeacon && navigator.sendBeacon(endpoint, body)) return;
    try {
      fetch(endpoint, { method: "POST", body: body, keepalive: true, credentials: "same-origin" });
    } catch (e) { /* analytics must never break the page */ }
  }

  function view(referrer) {
    var key = location.pathname + location.search;
    if (key === last) return;
    last = key;
    send(referrer);
  }

  view(document.referrer);

  // Single page sites: count a route change as a view.
  var from = location.href;
  function route() {
    var ref = from;
    from = location.href;
    view(ref);
  }
  ["pushState", "replaceState"].forEach(function (name) {
    var original = history[name];
    history[name] = function () {
      var result = original.apply(this, arguments);
      route();
      return result;
    };
  });
  addEventListener("popstate", route);
})();

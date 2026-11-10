// Live activity feed on the overview page: renders recent audit entries, receives new ones over
// server-sent events, and opens a slide-over panel with the full details (input, error, output).
(function () {
  "use strict";

  var root = document.getElementById("live-feed");
  if (!root) return;

  var MAX_ROWS = 100;
  var list = root.querySelector("[data-live-list]");
  var empty = root.querySelector("[data-live-empty]");
  var dot = root.querySelector("[data-live-dot]");
  var label = root.querySelector("[data-live-label]");
  var pauseButton = root.querySelector("[data-live-pause]");

  var lastId = 0;
  var paused = false;
  var queued = [];

  var badgeClass = { Success: "badge-green", Rejected: "badge-amber", Denied: "badge-red", Failed: "badge-red" };

  function el(tag, className, text) {
    var e = document.createElement(tag);
    if (className) e.className = className;
    if (text !== undefined && text !== null) e.textContent = text; // always text, never HTML
    return e;
  }

  function render(item, isNew) {
    var li = el("li");
    var row = el("button", "live-row" + (isNew ? " live-new" : ""));
    row.type = "button";
    row.dataset.id = item.id;
    row.title = "Show details";

    var time = new Date(item.timestamp);
    row.append(
      el("span", "w-20 shrink-0 font-mono text-xs text-slate-500 tabular-nums", time.toLocaleTimeString()),
      el("span", "w-20 shrink-0", null),
      el("span", "hidden w-52 shrink-0 truncate md:block", null),
      el("span", "min-w-0 flex-1 truncate font-mono text-xs text-slate-700",
        item.input || (item.tool === "list_connections" ? "(list connections)" : "(" + item.tool + ")")),
      el("span", "w-28 shrink-0 text-right text-xs text-slate-500 tabular-nums",
        (item.rows !== null && item.rows !== undefined ? item.rows + (item.rows === 1 ? " row · " : " rows · ") : "") + item.ms + " ms")
    );

    row.children[1].append(el("span", "badge " + (badgeClass[item.status] || "badge-slate"), item.status));

    var who = row.children[2];
    who.append(el("span", "text-slate-900", item.token));
    if (item.alias) {
      who.append(el("span", "text-slate-400", " → "));
      who.append(el("span", "font-mono text-xs text-slate-600", item.alias));
    }

    li.append(row);
    return li;
  }

  function add(item, isNew) {
    if (item.id <= lastId) return; // already shown (replayed after a reconnect)
    lastId = item.id;
    list.prepend(render(item, isNew));
    while (list.children.length > MAX_ROWS) list.lastElementChild.remove();
    empty.hidden = true;
  }

  // First render: entries embedded in the page, newest first.
  try {
    var initial = JSON.parse(document.getElementById("live-initial").textContent || "[]");
    for (var i = initial.length - 1; i >= 0; i--) add(initial[i], false);
  } catch (e) { /* start empty */ }

  function setState(state) {
    var states = {
      live: ["bg-emerald-500 animate-pulse", "Live"],
      paused: ["bg-amber-400", "Paused"],
      reconnecting: ["bg-amber-400", "Reconnecting…"]
    };
    dot.className = "h-2 w-2 rounded-full " + states[state][0];
    label.textContent = states[state][1];
  }

  function updatePauseButton() {
    pauseButton.textContent = paused ? (queued.length ? "Resume (" + queued.length + " new)" : "Resume") : "Pause";
  }

  pauseButton.addEventListener("click", function () {
    paused = !paused;
    if (!paused) {
      queued.forEach(function (item) { add(item, true); });
      queued = [];
    }
    setState(paused ? "paused" : (source.readyState === EventSource.OPEN ? "live" : "reconnecting"));
    updatePauseButton();
  });

  // The browser reconnects by itself and sends Last-Event-ID, so missed entries are replayed.
  var source = new EventSource(root.dataset.streamUrl + "?after=" + lastId);
  source.addEventListener("open", function () { if (!paused) setState("live"); });
  source.addEventListener("error", function () { if (!paused) setState("reconnecting"); });
  source.addEventListener("entry", function (e) {
    var item = JSON.parse(e.data);
    if (paused) { queued.push(item); updatePauseButton(); }
    else add(item, true);
  });

  // ---- Details panel ----
  var drawer = document.getElementById("audit-drawer");
  var drawerBody = drawer.querySelector("[data-drawer-body]");
  var drawerFull = drawer.querySelector("[data-drawer-full]");
  var lastFocus = null;

  async function openDetails(id) {
    lastFocus = document.activeElement;
    drawer.hidden = false;
    document.body.classList.add("overflow-hidden");
    drawerFull.href = root.dataset.detailsUrl + "/" + id;
    drawerBody.textContent = "Loading…";
    drawer.querySelector("button[data-drawer-close]").focus();

    try {
      var response = await fetch(root.dataset.panelUrl + "/" + id, { credentials: "same-origin" });
      if (response.redirected && response.url.indexOf("/account/login") >= 0) { location.reload(); return; }
      if (!response.ok) throw new Error("HTTP " + response.status);
      drawerBody.innerHTML = await response.text(); // server-rendered Razor, already HTML-encoded
    } catch (err) {
      drawerBody.textContent = "Could not load the details: " + err.message;
    }
  }

  function closeDetails() {
    drawer.hidden = true;
    drawerBody.textContent = "";
    document.body.classList.remove("overflow-hidden");
    if (lastFocus) lastFocus.focus();
  }

  list.addEventListener("click", function (e) {
    var row = e.target.closest("button[data-id]");
    if (row) openDetails(row.dataset.id);
  });
  drawer.querySelectorAll("[data-drawer-close]").forEach(function (b) { b.addEventListener("click", closeDetails); });
  document.addEventListener("keydown", function (e) {
    if (e.key === "Escape" && !drawer.hidden) closeDetails();
  });
})();

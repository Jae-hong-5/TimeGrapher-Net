/* TimeGrapher-Net manual — shared behaviour & chrome.
   Builds the sidebar + language toggle from one place so every page stays
   consistent, then wires:
   1) bilingual KO/EN toggle (persisted)
   2) screenshot fallback (placeholder when the image is missing)              */
(function () {
  "use strict";

  /* Single source of truth for navigation. `f` is the file under manual/
     (graph pages live in graphs/). */
  var START = [
    { f: "index.html",    t: "Overview" },
    { f: "index.html#signal-quality", t: "Signal quality" },
    { f: "controls.html", t: "Controls" }
  ];
  var GRAPHS = [
    { f: "graphs/rate-scope.html",   n: 1,  t: "Rate/Scope" },
    { f: "graphs/sound-print.html",  n: 2,  t: "Sound Print" },
    { f: "graphs/trace.html",        n: 3,  t: "Trace" },
    { f: "graphs/sweep.html",        n: 4,  t: "Sweep" },
    { f: "graphs/vario.html",        n: 5,  t: "Vario" },
    { f: "graphs/beat-error.html",   n: 6,  t: "Beat Error" },
    { f: "graphs/filter-scope.html", n: 7,  t: "Filter Scope" },
    { f: "graphs/long-term.html",    n: 8,  t: "Long-Term" },
    { f: "graphs/positions.html",    n: 9,  t: "Positions" },
    { f: "graphs/health.html",       n: 10, t: "Health" },
    { f: "graphs/beat-noise.html",   n: 11, t: "Beat Noise" },
    { f: "graphs/escapement.html",   n: 12, t: "Escapement" },
    { f: "graphs/waveforms.html",    n: 13, t: "Comparison" },
    { f: "graphs/spectrogram.html",  n: 14, t: "Spectrogram" }
  ];

  /* App version + manual build date — bump these when regenerating the manual/screenshots. */
  var APP_VERSION = "1.0.0";
  var BUILD_DATE = "2026-06-30";

  var inGraphs = /\/graphs\//.test(location.pathname);
  var prefix = inGraphs ? "../" : "";
  var here = location.pathname.split("/").pop() || "index.html";
  var hereKey = (inGraphs ? "graphs/" : "") + here;

  function link(item) {
    var active = item.f === hereKey ? " class=\"active\"" : "";
    var num = item.n ? "<span class=\"num\">" + item.n + "</span>" : "";
    return "<a href=\"" + prefix + item.f + "\"" + active + ">" + num + item.t + "</a>";
  }

  function buildSidebar() {
    var el = document.getElementById("sidebar");
    if (!el) return;
    var html =
      "<a class=\"brand\" href=\"" + prefix + "index.html\" style=\"text-decoration:none;color:inherit\">" +
      "<img src=\"" + prefix + "assets/logo.png\" alt=\"\" onerror=\"this.style.display='none'\">" +
      "<span><b>TimeGrapher</b><span>User Manual</span></span></a>" +
      "<div class=\"nav-group\"><h4>Getting started</h4>" +
      START.map(link).join("") + "</div>" +
      "<div class=\"nav-group\"><h4>Graphs</h4>" +
      GRAPHS.map(link).join("") + "</div>" +
      "<div class=\"nav-foot\"><b>App</b><br>v" + APP_VERSION +
      "<br><b>Build</b><br>" + BUILD_DATE + "</div>";
    el.innerHTML = html;
  }

  function buildLangToggle() {
    var el = document.getElementById("langToggle");
    if (!el) return;
    el.innerHTML =
      "<button data-mode=\"show-both\">전체</button>" +
      "<button data-mode=\"show-ko\">한국어</button>" +
      "<button data-mode=\"show-en\">EN</button>" +
      "<button data-mode=\"show-pt\">PT</button>";
  }

  /* ---- language toggle ---- */
  var KEY = "tg-manual-lang";
  var modes = ["show-both", "show-ko", "show-en", "show-pt"];
  function applyLang(mode) {
    modes.forEach(function (m) { document.body.classList.remove(m); });
    document.body.classList.add(mode);
    document.querySelectorAll(".lang-toggle button").forEach(function (b) {
      b.classList.toggle("active", b.getAttribute("data-mode") === mode);
    });
    try { localStorage.setItem(KEY, mode); } catch (e) {}
  }
  function initLang() {
    var saved = "show-both";
    try { saved = localStorage.getItem(KEY) || "show-both"; } catch (e) {}
    if (modes.indexOf(saved) < 0) saved = "show-both";
    applyLang(saved);
    document.querySelectorAll(".lang-toggle button").forEach(function (b) {
      b.addEventListener("click", function () { applyLang(b.getAttribute("data-mode")); });
    });
  }

  /* ---- screenshot fallback ---- */
  function initShots() {
    document.querySelectorAll("figure img[data-shot]").forEach(function (img) {
      img.addEventListener("error", function () {
        var fig = img.closest("figure");
        var src = img.getAttribute("src");
        var cap = fig ? fig.querySelector("figcaption") : null;
        var box = document.createElement("div");
        box.className = "shot-missing";
        box.innerHTML =
          "<b>스크린샷 준비 중 · Screenshot pending</b>" +
          "<span>예상 파일 · expected file:</span><code>" + src + "</code>";
        img.replaceWith(box);
        if (cap) box.after(cap);
      });
    });
  }

  document.addEventListener("DOMContentLoaded", function () {
    buildSidebar();
    buildLangToggle();
    initLang();
    initShots();
  });
})();

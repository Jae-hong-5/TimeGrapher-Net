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
    { f: "index.html",    t: "개요 · Overview" },
    { f: "controls.html", t: "왼쪽 메뉴 · Controls" }
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
    { f: "graphs/beat-noise.html",   n: 10, t: "Beat Noise" },
    { f: "graphs/escapement.html",   n: 11, t: "Escapement" },
    { f: "graphs/waveforms.html",    n: 12, t: "Waveforms" },
    { f: "graphs/spectrogram.html",  n: 13, t: "Spectrogram" }
  ];

  /* App version + manual build date — bump these when regenerating the manual/screenshots. */
  var APP_VERSION = "0.8.0";
  var BUILD_DATE = "2026-06-21";

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
      "<span><b>TimeGrapher</b><span>사용자 매뉴얼 · User Manual</span></span></a>" +
      "<div class=\"nav-group\"><h4>시작 · Getting started</h4>" +
      START.map(link).join("") + "</div>" +
      "<div class=\"nav-group\"><h4>그래프 · Graphs</h4>" +
      GRAPHS.map(link).join("") + "</div>" +
      "<div class=\"nav-foot\"><b>앱 · App</b><br>v" + APP_VERSION +
      "<br><b>빌드 · Build</b><br>" + BUILD_DATE + "</div>";
    el.innerHTML = html;
  }

  function buildLangToggle() {
    var el = document.getElementById("langToggle");
    if (!el) return;
    el.innerHTML =
      "<button data-mode=\"show-both\">둘 다</button>" +
      "<button data-mode=\"show-ko\">한국어</button>" +
      "<button data-mode=\"show-en\">EN</button>";
  }

  /* ---- language toggle ---- */
  var KEY = "tg-manual-lang";
  var modes = ["show-both", "show-ko", "show-en"];
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

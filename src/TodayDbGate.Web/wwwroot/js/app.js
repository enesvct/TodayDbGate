// Small progressive enhancements for the dashboard. Everything still works as plain forms without JS,
// except the connection tests, which return JSON.
(function () {
  "use strict";

  // Confirm destructive form submissions: <form data-confirm="Are you sure?">
  document.addEventListener("submit", function (e) {
    var form = e.target;
    if (form.dataset.confirm && !window.confirm(form.dataset.confirm)) e.preventDefault();
  });

  function showResult(el, result) {
    if (!el) return;
    el.textContent = result.success
      ? "✓ " + result.message + " (" + result.elapsedMs + " ms)"
      : "✗ " + result.message;
    el.className = el.className.replace(/\btext-(emerald|red|slate)-\d+\b/g, "").trim() +
      (result.success ? " text-emerald-700" : " text-red-700");
  }

  async function postForTest(url, body, resultEl, button) {
    button.disabled = true;
    if (resultEl) { resultEl.textContent = "Testing…"; resultEl.className += " text-slate-500"; }
    try {
      var response = await fetch(url, { method: "POST", body: body, credentials: "same-origin" });
      if (!response.ok) throw new Error("HTTP " + response.status);
      showResult(resultEl, await response.json());
    } catch (err) {
      showResult(resultEl, { success: false, message: "Request failed: " + err.message });
    } finally {
      button.disabled = false;
    }
  }

  // "Test" buttons in the connection list: <form data-ajax-test> next to a [data-test-result] element.
  document.querySelectorAll("form[data-ajax-test]").forEach(function (form) {
    form.addEventListener("submit", function (e) {
      e.preventDefault();
      var resultEl = form.parentElement.querySelector("[data-test-result]");
      postForTest(form.action, new FormData(form), resultEl, form.querySelector("button"));
    });
  });

  // "Test connection" on the connection form: posts the current (unsaved) values.
  document.querySelectorAll("[data-test-form]").forEach(function (button) {
    button.addEventListener("click", function () {
      var form = button.closest("form");
      postForTest(button.dataset.testForm, new FormData(form), form.querySelector("[data-test-result]"), button);
    });
  });

  // Connection form: default port placeholder and SQL Server-only fields follow the database type.
  var typeSelect = document.querySelector("[data-db-type]");
  if (typeSelect) {
    var defaultPorts = { PostgreSql: "5432", SqlServer: "1433", MySql: "3306" };
    var portInput = document.querySelector("[data-port]");
    var sqlServerOnly = document.querySelectorAll("[data-sqlserver-only]");
    var update = function () {
      if (portInput) portInput.placeholder = defaultPorts[typeSelect.value] || "";
      sqlServerOnly.forEach(function (el) { el.hidden = typeSelect.value !== "SqlServer"; });
    };
    typeSelect.addEventListener("change", update);
    update();
  }

  // Copy buttons: <button data-copy="#input-id">
  document.querySelectorAll("[data-copy]").forEach(function (button) {
    button.addEventListener("click", async function () {
      var source = document.querySelector(button.dataset.copy);
      if (!source) return;
      var original = button.textContent;
      try {
        await navigator.clipboard.writeText(source.value || source.textContent);
        button.textContent = "Copied";
      } catch {
        source.select && source.select();
        button.textContent = "Press Ctrl+C";
      }
      setTimeout(function () { button.textContent = original; }, 1500);
    });
  });

  document.querySelectorAll("[data-select-on-focus]").forEach(function (input) {
    input.addEventListener("focus", function () { input.select(); });
  });
})();

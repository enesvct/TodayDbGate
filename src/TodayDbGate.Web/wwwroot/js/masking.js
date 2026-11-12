// Masking page: filter tables, apply name-based suggestions, expand/collapse, and count unsaved changes.
(function () {
  "use strict";

  var form = document.getElementById("masking-form");
  if (!form) return;

  var tables = Array.prototype.slice.call(form.querySelectorAll("details[data-mask-table]"));
  var selects = Array.prototype.slice.call(form.querySelectorAll("select[data-mask-select]"));
  var dirtyLabel = form.querySelector("[data-mask-dirty]");

  function updateDirty() {
    var changed = 0;
    selects.forEach(function (s) {
      var isChanged = s.value !== s.dataset.initial;
      s.classList.toggle("ring-2", isChanged);
      s.classList.toggle("ring-amber-300", isChanged);
      if (isChanged) changed++;
    });
    if (dirtyLabel) dirtyLabel.textContent = changed ? changed + " unsaved change" + (changed === 1 ? "" : "s") : "no unsaved changes";
  }
  selects.forEach(function (s) { s.addEventListener("change", updateDirty); });
  updateDirty();

  var filter = form.querySelector("[data-mask-filter]");
  if (filter) {
    filter.addEventListener("input", function () {
      var q = filter.value.trim().toLowerCase();
      tables.forEach(function (t) { t.hidden = q !== "" && t.dataset.maskTable.indexOf(q) < 0; });
    });
  }

  var suggest = form.querySelector("[data-mask-suggest]");
  if (suggest) {
    suggest.addEventListener("click", function () {
      selects.forEach(function (s) {
        if (s.dataset.suggested && s.value === "Default") {
          s.value = s.dataset.suggested;
          var table = s.closest("details");
          if (table) table.open = true;
        }
      });
      updateDirty();
      suggest.disabled = true;
      suggest.textContent = "Suggestions applied — review and save";
    });
  }

  var expand = form.querySelector("[data-mask-expand]");
  var collapse = form.querySelector("[data-mask-collapse]");
  if (expand) expand.addEventListener("click", function () { tables.forEach(function (t) { if (!t.hidden) t.open = true; }); });
  if (collapse) collapse.addEventListener("click", function () { tables.forEach(function (t) { t.open = false; }); });

  // Warn before leaving with unsaved changes.
  var submitting = false;
  form.addEventListener("submit", function () { submitting = true; });
  window.addEventListener("beforeunload", function (e) {
    if (submitting) return;
    if (selects.some(function (s) { return s.value !== s.dataset.initial; })) { e.preventDefault(); e.returnValue = ""; }
  });
})();

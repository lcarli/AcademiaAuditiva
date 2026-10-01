// Shows the filter selects of the exercise chosen in the routine item form.
// Each exercise has its own <fieldset data-exercise-id>; only the active one is
// enabled, so the others are not submitted.
(function (document) {
  const exerciseSelect = document.getElementById("ExerciseId");
  if (!exerciseSelect) return;

  const fieldsets = Array.from(document.querySelectorAll("fieldset[data-exercise-id]"));
  const none = document.querySelector("[data-exercise-none]");

  function sync() {
    const selected = exerciseSelect.value;
    let found = false;
    fieldsets.forEach((fieldset) => {
      const active = fieldset.dataset.exerciseId === selected;
      fieldset.disabled = !active;
      fieldset.hidden = !active;
      found = found || active;
    });
    if (none) none.hidden = found;
  }

  exerciseSelect.addEventListener("change", sync);
  sync();
})(document);

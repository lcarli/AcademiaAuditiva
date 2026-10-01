/**
 * Helpers for translating AA's note format ("C4", "Eb5", "F#3") to
 * VexFlow 5's key format ("c/4", "eb/5", "f#/3"). Centralised here so
 * both StaffRenderer and StaffEditor agree on encoding.
 */
(function (root) {
  "use strict";

  /**
   * Convert an AA note name to a VexFlow key string.
   * "C4"  -> "c/4"
   * "F#3" -> "f#/3"
   * "Eb5" -> "eb/5"
   * Returns null for unparseable input.
   */
  function noteToVexKey(note) {
    if (!note || typeof note !== "string") return null;
    var m = note.match(/^([A-Ga-g])([#b]?)(-?\d+)$/);
    if (!m) return null;
    return m[1].toLowerCase() + "/" + m[3];
  }

  /**
   * Translate a duration label to VexFlow's duration string.
   * The trailing "r" marker (used by StaffRenderer's caller) is
   * preserved by VexFlow when the user passes the full label, but
   * StaffRenderer drops it explicitly and adds a rest-flag instead.
   */
  function isRestLabel(label) {
    return typeof label === "string" && label.endsWith("r");
  }

  function stripRest(label) {
    return isRestLabel(label) ? label.slice(0, -1) : label;
  }

  function isDottedLabel(label) {
    return typeof label === "string" && stripRest(label).endsWith(".");
  }

  function baseDuration(label) {
    var l = stripRest(label);
    if (l.endsWith(".")) l = l.slice(0, -1);
    return l;
  }

  root.StaffMapping = {
    noteToVexKey: noteToVexKey,
    isRestLabel: isRestLabel,
    stripRest: stripRest,
    isDottedLabel: isDottedLabel,
    baseDuration: baseDuration,
  };
})(window);

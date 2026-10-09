// Reusable A/B playback for comparative technical-listening rounds.
// The server returns only opaque { key, token } pairs. Keys name controls
// ("A", "B"), never which clip is the reference or the processed answer.
(function (window, document) {
  "use strict";

  function format(template, value) {
    return String(template || "").replace("{0}", value);
  }

  function isTyping(target) {
    return !!target?.closest?.("input, select, textarea, [contenteditable='true']");
  }

  function create(root) {
    if (!root || root.aaABPlayback) return root?.aaABPlayback || null;

    const playButtons = [...root.querySelectorAll("[data-aa-ab-play]")];
    const replayButton = root.querySelector("[data-aa-ab-replay]");
    const status = root.querySelector("[data-aa-ab-status]");
    const tokens = new Map();
    let lastKey = null;
    let playSequence = 0;
    let destroyed = false;

    function say(text, state = "") {
      if (status) status.textContent = text || "";
      root.dataset.state = state;
    }

    function buttonFor(key) {
      return playButtons.find((button) => button.dataset.aaAbPlay === key);
    }

    function markPlaying(key) {
      playButtons.forEach((button) => {
        button.setAttribute("aria-pressed", button.dataset.aaAbPlay === key ? "true" : "false");
      });
    }

    async function play(key) {
      const token = tokens.get(key);
      if (!token || destroyed) return false;

      const sequence = ++playSequence;
      lastKey = key;
      if (replayButton) replayButton.disabled = false;
      markPlaying(key);
      say(format(root.dataset.loading, key), "loading");

      try {
        await AudioEngine.playToken(token, {
          onStart() {
            if (sequence === playSequence && !destroyed) {
              say(format(root.dataset.playing, key), "playing");
            }
          },
        });
        if (sequence === playSequence && !destroyed) {
          markPlaying("");
          say(root.dataset.ready, "ready");
        }
        return true;
      } catch (error) {
        if (sequence === playSequence && !destroyed) {
          markPlaying("");
          say(root.dataset.error, "error");
          root.dispatchEvent(new CustomEvent("aa:ab-error", { detail: { key, error } }));
        }
        return false;
      }
    }

    function setRound(clips) {
      const next = new Map();
      for (const clip of clips || []) {
        if (!clip || typeof clip.key !== "string" || typeof clip.token !== "string"
            || !clip.key || !clip.token || next.has(clip.key) || !buttonFor(clip.key)) {
          throw new Error("The A/B round has invalid clips.");
        }
        next.set(clip.key, clip.token);
      }
      if (next.size !== playButtons.length) {
        throw new Error("The A/B round does not have every playback control.");
      }

      AudioEngine.stop();
      playSequence += 1;
      tokens.clear();
      next.forEach((token, key) => tokens.set(key, token));
      lastKey = null;
      markPlaying("");
      playButtons.forEach((button) => { button.disabled = false; });
      if (replayButton) replayButton.disabled = true;
      say(root.dataset.ready, "ready");
      next.forEach((token) => AudioEngine.preload(token));
    }

    function clear() {
      AudioEngine.stop();
      playSequence += 1;
      tokens.clear();
      lastKey = null;
      markPlaying("");
      playButtons.forEach((button) => { button.disabled = true; });
      if (replayButton) replayButton.disabled = true;
      say("", "");
    }

    function replay() {
      return lastKey ? play(lastKey) : Promise.resolve(false);
    }

    function onKeydown(event) {
      if (destroyed || event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey
          || isTyping(event.target)) return;

      const key = event.key.length === 1 ? event.key.toUpperCase() : event.key;
      if (tokens.has(key)) {
        event.preventDefault();
        play(key);
      } else if (event.key === " " && lastKey) {
        event.preventDefault();
        replay();
      }
    }

    playButtons.forEach((button) => {
      button.addEventListener("click", () => play(button.dataset.aaAbPlay));
    });
    replayButton?.addEventListener("click", replay);
    document.addEventListener("keydown", onKeydown);

    const api = {
      setRound,
      clear,
      play,
      replay,
      destroy() {
        if (destroyed) return;
        clear();
        destroyed = true;
        document.removeEventListener("keydown", onKeydown);
        delete root.aaABPlayback;
      },
    };
    root.aaABPlayback = api;
    return api;
  }

  function init() {
    document.querySelectorAll("[data-aa-ab]").forEach(create);
  }

  window.ABPlayback = { create };
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
  else init();
})(window, document);

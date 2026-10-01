// XP, level and badge feedback after an exercise answer. ValidateExercise
// returns `rewards` with every text already localized (see
// ExerciseController.BuildRewards); this file only renders it.
(function (window, document) {
  "use strict";

  const GROUPS = ["dedication", "mastery", "progress", "fun"];

  function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text != null) node.textContent = text;
    return node;
  }

  function icon(name) {
    const node = el("i", "bi " + (/^bi-[a-z0-9-]+$/.test(String(name || "")) ? name : "bi-award"));
    node.setAttribute("aria-hidden", "true");
    return node;
  }

  function rank(symbol, size) {
    const node = el("span", "aa-rank" + (size ? " aa-rank-" + size : ""), symbol);
    node.setAttribute("aria-hidden", "true");
    return node;
  }

  function progressBar(percent, label) {
    const value = Math.max(0, Math.min(100, Math.round(Number(percent) || 0)));
    const bar = el("div", "progress aa-xp-bar");
    bar.setAttribute("role", "progressbar");
    bar.setAttribute("aria-label", label || "");
    bar.setAttribute("aria-valuemin", "0");
    bar.setAttribute("aria-valuemax", "100");
    bar.setAttribute("aria-valuenow", String(value));
    const fill = el("div", "progress-bar");
    fill.style.width = value + "%";
    bar.append(fill);
    return bar;
  }

  function footer(r) {
    const root = el("div", "aa-reward-footer");
    const level = el("span", "aa-reward-level");
    level.append(rank(r.rank.symbol, "sm"), el("span", null, r.levelText + " · " + r.rank.name));
    root.append(el("span", "aa-xp-chip", r.xpGainedText), level, progressBar(r.levelPercent, r.levelText));
    return root;
  }

  function medal(badge) {
    const group = GROUPS.includes(badge.group) ? badge.group : "mastery";
    const item = el("li", "aa-reward-medal aa-medal-" + group);
    const disc = el("span", "aa-medal-disc");
    disc.append(icon(badge.icon));
    const body = el("div", "aa-reward-medal-body");
    body.append(el("strong", null, badge.title), el("span", null, badge.description));
    item.append(disc, body);
    return item;
  }

  // Only same-site paths: the URL comes from the server, but never navigate elsewhere.
  function localPath(url) {
    return typeof url === "string" && url.startsWith("/") && !url.startsWith("//") ? url : null;
  }

  function celebrate(r) {
    const c = r.celebration;
    if (!c || !window.Swal) return Promise.resolve();

    const content = el("div", "aa-celebration");
    if (r.levelUp) {
      content.append(rank(r.rank.symbol, "lg"));
      if (c.text) content.append(el("p", "aa-celebration-text", c.text));
    }
    if (Array.isArray(r.badges) && r.badges.length) {
      if (c.badgesHeading) content.append(el("p", "aa-celebration-heading", c.badgesHeading));
      const list = el("ul", "aa-reward-medals");
      r.badges.forEach((badge) => list.append(medal(badge)));
      content.append(list);
    }

    const url = localPath(c.viewAllUrl);
    return Swal.fire({
      title: c.title,
      html: content,
      customClass: { popup: "aa-celebration-popup" },
      showCancelButton: !!url,
      confirmButtonText: url ? c.viewAllText : c.closeText,
      cancelButtonText: c.closeText,
      focusCancel: !!url
    }).then((result) => {
      if (url && result.isConfirmed) window.location.assign(url);
    });
  }

  window.AARewards = {
    celebrate,

    // Adds the XP footer to an answer dialog and, when the answer unlocked a
    // level or badge, opens the celebration once that dialog closes.
    decorate(options, data) {
      const r = data && data.rewards;
      if (!r || !r.rank) return options;

      try {
        options.footer = footer(r);
      } catch (err) {
        console.error("Could not render the XP footer:", err);
      }

      if (r.celebration) {
        const didClose = options.didClose;
        options.didClose = function () {
          if (typeof didClose === "function") didClose.apply(this, arguments);
          // SweetAlert2 tears down a dialog opened synchronously from didClose.
          window.setTimeout(() => celebrate(r), 0);
        };
      }
      return options;
    }
  };
})(window, document);

// XP, level, badge and learning path feedback after an exercise answer.
// ValidateExercise returns `rewards` and `path` with every text already
// localized (see ExerciseController.BuildRewards and BuildPathFeedback);
// this file only renders them.
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

  function pathFooter(p) {
    const text = p.label + " · " + p.text;
    const root = el("div", "aa-path-footer" + (p.completed ? " is-complete" : ""));
    root.append(
      icon(p.completed ? "bi-check2-circle" : "bi-signpost-split"),
      el("span", "aa-path-footer-text", text),
      progressBar(p.percent, text));
    return root;
  }

  function answerFooter(r, p) {
    if (!p) return footer(r);
    const root = el("div", "aa-answer-footer");
    if (r) root.append(footer(r));
    root.append(pathFooter(p));
    return root;
  }

  function medal(badge) {
    const group = GROUPS.includes(badge.group) ? badge.group : "mastery";
    const item = el("li", "aa-reward-medal aa-medal-" + group);
    const disc = el("span", "aa-medal-disc");
    const src = localPath(badge.image);
    if (src) {
      const art = el("img");
      art.src = src;
      art.alt = "";
      disc.append(art);
    }
    const body = el("div", "aa-reward-medal-body");
    body.append(el("strong", null, badge.title), el("span", null, badge.description));
    item.append(disc, body);
    return item;
  }

  // Fetched while the answer dialog is open, so the celebration shows the art at once.
  function preloadArt(badges) {
    badges.forEach((badge) => {
      const src = localPath(badge.image);
      if (src) new Image().src = src;
    });
  }

  // Only same-site paths: the URL comes from the server, but never navigate elsewhere.
  function localPath(url) {
    return typeof url === "string" && url.startsWith("/") && !url.startsWith("//") ? url : null;
  }

  // One dialog for everything the answer unlocked: the learning path step
  // first (with the way to the next step), then the level and the badges.
  function celebrate(r, p) {
    const rc = r && r.celebration;
    const pc = p && p.celebration;
    if ((!rc && !pc) || !window.Swal) return Promise.resolve();

    const content = el("div", "aa-celebration");
    if (pc) {
      const disc = el("span", "aa-path-disc");
      disc.append(icon(pc.icon));
      content.append(disc);
      if (pc.text) content.append(el("p", "aa-celebration-text", pc.text));
      if (pc.nextText) content.append(el("p", "aa-celebration-next", pc.nextText));
    }
    if (rc && r.levelUp) {
      if (pc) content.append(el("p", "aa-celebration-heading", rc.title));
      content.append(rank(r.rank.symbol, pc ? null : "lg"));
      if (rc.text) content.append(el("p", "aa-celebration-text", rc.text));
    }
    if (rc && Array.isArray(r.badges) && r.badges.length) {
      const heading = rc.badgesHeading || (pc ? rc.title : null);
      if (heading) content.append(el("p", "aa-celebration-heading", heading));
      const list = el("ul", "aa-reward-medals");
      r.badges.forEach((badge) => list.append(medal(badge)));
      content.append(list);
    }

    const nextUrl = pc ? localPath(pc.actionUrl) : null;
    const viewAllUrl = rc ? localPath(rc.viewAllUrl) : null;
    const closeText = (pc && pc.closeText) || (rc && rc.closeText) || "OK";
    const options = {
      title: pc ? pc.title : rc.title,
      html: content,
      customClass: { popup: "aa-celebration-popup" },
      confirmButtonText: closeText
    };
    let confirmUrl = null;
    let denyUrl = null;
    if (nextUrl) {
      confirmUrl = nextUrl;
      options.confirmButtonText = pc.actionText;
      if (viewAllUrl) {
        denyUrl = viewAllUrl;
        Object.assign(options, { showDenyButton: true, denyButtonText: rc.viewAllText });
      }
    } else if (viewAllUrl) {
      confirmUrl = viewAllUrl;
      options.confirmButtonText = rc.viewAllText;
    }
    if (confirmUrl) {
      Object.assign(options, { showCancelButton: true, cancelButtonText: closeText, focusCancel: true });
    }

    return Swal.fire(options).then((result) => {
      if (result.isConfirmed && confirmUrl) window.location.assign(confirmUrl);
      else if (result.isDenied && denyUrl) window.location.assign(denyUrl);
    });
  }

  window.AARewards = {
    celebrate,

    // Adds the XP and learning path footer to an answer dialog and, when the
    // answer unlocked a level, a badge or a path step, opens the celebration
    // once that dialog closes.
    decorate(options, data) {
      const r = data && data.rewards && data.rewards.rank ? data.rewards : null;
      const p = data && data.path && data.path.label ? data.path : null;
      if (!r && !p) return options;

      try {
        options.footer = answerFooter(r, p);
      } catch (err) {
        console.error("Could not render the answer footer:", err);
      }

      if ((r && r.celebration) || (p && p.celebration)) {
        if (r && Array.isArray(r.badges)) preloadArt(r.badges);
        const didClose = options.didClose;
        options.didClose = function () {
          if (typeof didClose === "function") didClose.apply(this, arguments);
          // SweetAlert2 tears down a dialog opened synchronously from didClose.
          window.setTimeout(() => celebrate(r, p), 0);
        };
      }
      return options;
    }
  };
})(window, document);

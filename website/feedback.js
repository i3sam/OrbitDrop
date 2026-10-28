// The help & feedback page: get help, report a bug or request a feature, then send it to /api/feedback. Opened from
// the app (?topic=help|bug|purchase|feature&v=…&edition=store|github&os=<build>&pasted=1), it starts on the right tab
// with OrbitDrop's version, edition and Windows filled in.
// Everything a visitor types is sent as JSON text and never put into the page as HTML.
(() => {
  "use strict";
  const form = document.getElementById("fb-form");
  if (!form) return;
  const done = document.querySelector(".fb-done");
  const status = form.querySelector(".fb-status");
  const send = form.querySelector('button[type="submit"]');
  const tabs = [...document.querySelectorAll(".fb-switch [role=tab]")];
  const opened = Date.now();

  function show(type) {
    form.elements.type.value = type;
    tabs.forEach((t) => t.setAttribute("aria-selected", String(t.dataset.type === type)));
    form.querySelectorAll("[data-for]").forEach((el) => { el.hidden = el.dataset.for !== type; });
    form.querySelector(".fb-bug").hidden = type === "feature";
    form.querySelector(".fb-help").hidden = type !== "help";
    form.elements.title.placeholder = type === "bug" ? "e.g. CDR to PNG says “That didn't work”"
      : type === "help" ? "e.g. My upgrade isn't showing as unlocked" : "e.g. Convert HEIC to JPG";
    status.textContent = "";
    if (location.hash !== `#${type}`) history.replaceState(null, "", `#${type}`);
  }
  tabs.forEach((t) => t.addEventListener("click", () => show(t.dataset.type)));

  // Opened from the app: the tab it asked for and what it knows (never anything personal).
  const q = new URLSearchParams(location.search);
  const topic = q.get("topic") ?? location.hash.slice(1);
  if (topic === "purchase") form.elements.about.value = "purchase";
  const v = (q.get("v") ?? "").slice(0, 40);
  if (/^[\w.+-]+$/.test(v)) form.elements.version.value = v;
  const edition = { store: "Microsoft Store", github: "GitHub" }[q.get("edition")];
  if (edition) form.elements.source.value = edition;
  const build = Number(q.get("os"));
  if (build > 0) form.elements.windows.value = build >= 22000 ? "Windows 11" : "Windows 10";
  form.elements.from.value = (q.get("from") ?? "").replace(/[^a-z]/g, "").slice(0, 20);
  form.querySelector(".fb-pasted").hidden = q.get("pasted") !== "1";
  show(topic === "bug" || topic === "feature" ? topic : "help");

  const count = form.querySelector('[data-count="details"]');
  form.elements.details.addEventListener("input", () => { count.textContent = `${form.elements.details.value.length} / 4000`; });

  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    const f = form.elements;
    if (f.title.value.trim().length < 3) { status.textContent = "Please give it a short title."; f.title.focus(); return; }
    if (f.details.value.trim().length < 10) { status.textContent = "Please add a few more details."; f.details.focus(); return; }
    if (f.type.value === "help" && !f.email.value.trim()) { status.textContent = "Please add your email, so we can reply."; f.email.focus(); return; }
    if (f.email.value && !f.email.checkValidity()) { status.textContent = "That email address doesn't look right."; f.email.focus(); return; }

    const body = {
      type: f.type.value, title: f.title.value, details: f.details.value, email: f.email.value,
      steps: f.steps.value, version: f.version.value, windows: f.windows.value, source: f.source.value,
      about: f.about.value, from: f.from.value, website: f.website.value, elapsed: Date.now() - opened,
    };
    if (body.type !== "bug") body.steps = "";
    if (body.type === "feature") body.version = body.windows = body.source = "";
    if (body.type !== "help") body.about = "";

    send.disabled = true;
    status.textContent = "Sending…";
    try {
      const r = await fetch("/api/feedback", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
      const result = await r.json().catch(() => ({}));
      if (r.ok && result.ok) {
        form.reset();
        count.textContent = "0 / 4000";
        form.hidden = true;
        done.hidden = false;
        done.querySelector("h2").focus?.();
      } else {
        status.textContent = (result.error || "It couldn't be sent just now.") + " You can also open an issue on GitHub.";
      }
    } catch {
      status.textContent = "It couldn't be sent. Check your connection and try again.";
    } finally {
      send.disabled = false;
    }
  });

  document.getElementById("fb-again").addEventListener("click", () => {
    done.hidden = true;
    form.hidden = false;
    status.textContent = "";
    show(form.elements.type.value);
  });
})();

// POST /api/feedback: a help request, bug report or feature request from orbitdrop.essam.biz/feedback, emailed privately to the
// developer through Resend (https://resend.com). Nothing is stored here, and nothing is ever shown back to anyone.
//
// Settings (Vercel → Project → Settings → Environment Variables; never in this repository):
//   RESEND_API_KEY   the Resend API key
//   FEEDBACK_TO      the address the reports go to
//   FEEDBACK_FROM    optional sender, e.g. "OrbitDrop <feedback@yourdomain>"; default: Resend's own test sender,
//                    which can only deliver to the Resend account's own address (exactly what's wanted here)
//
// Defences: same-site Origin only, JSON only, strict length limits, a hidden honeypot field, a minimum time on the
// page, per-address and overall rate limits, control characters stripped, plain-text email only (no HTML, so
// nothing in a report can run or render), CR/LF removed from the subject, generic error messages.

const ALLOWED_ORIGINS = new Set(["https://orbitdrop.essam.biz", "http://localhost:4321", "http://localhost:8765"]);
const LIMITS = { title: 120, details: 4000, steps: 3000, email: 200, version: 40, windows: 60, source: 20, from: 20 };
const ABOUT = { purchase: "The upgrade or a purchase", using: "Using OrbitDrop", other: "Something else" };
const LABELS = { help: "Help request", bug: "Bug report", feature: "Feature request" };
const PER_ADDRESS = { max: 4, windowMs: 10 * 60_000 };  // 4 reports per address per 10 minutes
const OVERALL = { max: 40, windowMs: 60 * 60_000 };     // and 40 per hour in total (protects the email quota)
const MIN_FILL_MS = 2500;                                // humans take longer than this to fill a form

// Kept per running instance; good enough against floods from one place, and nothing about anyone is kept for long.
const recent = new Map();
let overall = [];

function json(res, status, body) {
  res.statusCode = status;
  res.setHeader("Content-Type", "application/json; charset=utf-8");
  res.setHeader("Cache-Control", "no-store");
  res.end(JSON.stringify(body));
}

/** Plain text only: no control characters except newlines and tabs, trimmed, capped. */
export function clean(value, max) {
  if (typeof value !== "string") return "";
  return value.replace(/\r\n?/g, "\n").replace(/[\u0000-\u0008\u000B-\u001F\u007F​-‏‪-‮⁦-⁩]/g, "").trim().slice(0, max);
}

export function validEmail(s) {
  return s.length <= LIMITS.email && /^[^\s@<>()",;:]+@[^\s@<>()",;:]+\.[a-z]{2,}$/i.test(s);
}

function limited(key, now) {
  const hits = (recent.get(key) ?? []).filter((t) => now - t < PER_ADDRESS.windowMs);
  overall = overall.filter((t) => now - t < OVERALL.windowMs);
  if (hits.length >= PER_ADDRESS.max || overall.length >= OVERALL.max) { recent.set(key, hits); return true; }
  hits.push(now); overall.push(now); recent.set(key, hits);
  if (recent.size > 5000) recent.clear(); // never grows without bound
  return false;
}

/** Checks a submission; returns the email to send, or an error for the visitor. */
export function build(body) {
  if (!body || typeof body !== "object") return { error: "Something's missing." };
  const type = Object.hasOwn(LABELS, body.type) ? body.type : null;
  if (!type) return { error: "Something's missing." };
  const about = type === "help" ? (Object.hasOwn(ABOUT, body.about) ? body.about : "other") : null;
  const f = {
    title: clean(body.title, LIMITS.title), details: clean(body.details, LIMITS.details), steps: clean(body.steps, LIMITS.steps),
    email: clean(body.email, LIMITS.email), version: clean(body.version, LIMITS.version), windows: clean(body.windows, LIMITS.windows),
    source: clean(body.source, LIMITS.source), from: clean(body.from, LIMITS.from).replace(/[^a-z]/g, ""),
  };
  if (f.title.length < 3) return { error: "Please give it a short title." };
  if (f.details.length < 10) return { error: "Please add a few more details." };
  if (type === "help" && !f.email) return { error: "Please add your email, so we can reply." };
  if (f.email && !validEmail(f.email)) return { error: "That email address doesn't look right." };

  // Purchase problems stand out in the inbox: someone paid and is stuck.
  const label = about === "purchase" ? "PURCHASE help" : LABELS[type];
  const subject = `[OrbitDrop] ${label}: ${f.title}`.replace(/[\r\n]+/g, " ").slice(0, 160);
  const lines = [
    `${label}`, "", ...(about ? [`About: ${ABOUT[about]}`] : []), `Title: ${f.title}`, "",
    type === "feature" ? "The idea:" : "What happened:", f.details, "",
  ];
  if (type !== "feature") {
    if (type === "bug" && f.steps) lines.push("Steps to make it happen:", f.steps, "");
    lines.push(`OrbitDrop version: ${f.version || "not given"}`, `Windows: ${f.windows || "not given"}`, `Installed from: ${f.source || "not given"}`, "");
  }
  lines.push(`Reply to: ${f.email || "no email given"}`, "",
    f.from ? `Sent from the form at orbitdrop.essam.biz/feedback, opened from the app (${f.from}).` : "Sent from the form at orbitdrop.essam.biz/feedback.");
  return { email: { subject, text: lines.join("\n"), replyTo: f.email || undefined } };
}

export default async function handler(req, res) {
  if (req.method !== "POST") { res.setHeader("Allow", "POST"); return json(res, 405, { ok: false, error: "Use the form." }); }
  const origin = req.headers.origin ?? "";
  if (!ALLOWED_ORIGINS.has(origin)) return json(res, 403, { ok: false, error: "Use the form on orbitdrop.essam.biz." });
  if (!String(req.headers["content-type"] ?? "").startsWith("application/json")) return json(res, 415, { ok: false, error: "Use the form." });

  let body = req.body;
  if (typeof body === "string") { if (body.length > 20_000) return json(res, 413, { ok: false, error: "That's too long." }); try { body = JSON.parse(body); } catch { body = null; } }
  if (JSON.stringify(body ?? {}).length > 20_000) return json(res, 413, { ok: false, error: "That's too long." });

  // Bots fill every field and submit instantly; pretend it worked so they learn nothing.
  if (body?.website || !(Number(body?.elapsed) >= MIN_FILL_MS)) return json(res, 200, { ok: true });

  const address = String(req.headers["x-real-ip"] ?? req.headers["x-forwarded-for"] ?? "").split(",")[0].trim() || "unknown";
  if (limited(address, Date.now())) return json(res, 429, { ok: false, error: "That's a lot of reports in a short time. Please try again in a few minutes." });

  const built = build(body);
  if (built.error) return json(res, 400, { ok: false, error: built.error });

  const key = process.env.RESEND_API_KEY, to = process.env.FEEDBACK_TO;
  if (!key || !to) return json(res, 503, { ok: false, error: "The form isn't set up yet." });

  try {
    const r = await fetch("https://api.resend.com/emails", {
      method: "POST",
      headers: { Authorization: `Bearer ${key}`, "Content-Type": "application/json" },
      body: JSON.stringify({
        from: process.env.FEEDBACK_FROM || "OrbitDrop feedback <onboarding@resend.dev>",
        to: [to], subject: built.email.subject, text: built.email.text,
        ...(built.email.replyTo ? { reply_to: built.email.replyTo } : {}),
      }),
      signal: AbortSignal.timeout(10_000),
    });
    if (!r.ok) { console.error(`Resend refused the email: HTTP ${r.status}`); return json(res, 502, { ok: false, error: "It couldn't be sent just now." }); }
    return json(res, 200, { ok: true });
  } catch {
    console.error("Resend could not be reached");
    return json(res, 502, { ok: false, error: "It couldn't be sent just now." });
  }
}

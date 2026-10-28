// Tests for api/feedback.js: node --test website/tests
import { test } from "node:test";
import assert from "node:assert/strict";
import handler, { build, clean } from "../api/feedback.js";

function call(body, { origin = "https://orbitdrop.essam.biz", method = "POST", type = "application/json", ip = "203.0.113.1" } = {}) {
  return new Promise((resolve) => {
    const res = { statusCode: 0, headers: {}, setHeader(k, v) { this.headers[k] = v; }, end(s) { resolve({ status: this.statusCode, body: JSON.parse(s), headers: this.headers }); } };
    handler({ method, headers: { origin, "content-type": type, "x-real-ip": ip }, body }, res);
  });
}
const good = { type: "bug", title: "CDR to PNG fails", details: "It says that didn't work every time.", elapsed: 9000, website: "" };

test("strips control characters and caps length", () => {
  assert.equal(clean("a\u0000b‮c\r\nd", 10), "abc\nd");
  assert.equal(clean("x".repeat(50), 5), "xxxxx");
  assert.equal(clean(42, 5), "");
});

test("the subject can't carry extra email headers", () => {
  const { email } = build({ ...good, title: "Hi\r\nBcc: victim@example.com" });
  assert.ok(!/[\r\n]/.test(email.subject));
});

test("rejects other sites, other methods and non-JSON", async () => {
  assert.equal((await call(good, { origin: "https://evil.example" })).status, 403);
  assert.equal((await call(good, { origin: "" })).status, 403);
  assert.equal((await call(good, { method: "GET" })).status, 405);
  assert.equal((await call(good, { type: "text/plain", ip: "203.0.113.2" })).status, 415);
});

test("bots get a fake success and nothing is sent", async () => {
  let sent = 0; globalThis.fetch = async () => { sent++; return { ok: true }; };
  process.env.RESEND_API_KEY = "test"; process.env.FEEDBACK_TO = "owner@example.com";
  assert.deepEqual((await call({ ...good, website: "http://spam" }, { ip: "203.0.113.3" })).body, { ok: true });
  assert.deepEqual((await call({ ...good, elapsed: 200 }, { ip: "203.0.113.3" })).body, { ok: true });
  assert.equal(sent, 0);
});

test("validates fields", async () => {
  assert.equal((await call({ ...good, title: "x" }, { ip: "203.0.113.4" })).status, 400);
  assert.equal((await call({ ...good, email: "not an email" }, { ip: "203.0.113.4" })).status, 400);
  assert.equal((await call({ ...good, type: "admin" }, { ip: "203.0.113.4" })).status, 400);
  assert.equal((await call("x".repeat(30000), { ip: "203.0.113.4" })).status, 413);
});

test("sends one plain-text email to the configured address, with the visitor as reply-to", async () => {
  let payload; globalThis.fetch = async (url, init) => { payload = { url, ...JSON.parse(init.body), auth: init.headers.Authorization }; return { ok: true }; };
  process.env.RESEND_API_KEY = "re_test"; process.env.FEEDBACK_TO = "owner@example.com";
  const r = await call({ ...good, email: "fan@example.com", steps: "1. drag\n2. shift" }, { ip: "203.0.113.5" });
  assert.equal(r.status, 200);
  assert.equal(payload.url, "https://api.resend.com/emails");
  assert.deepEqual(payload.to, ["owner@example.com"]);
  assert.equal(payload.reply_to, "fan@example.com");
  assert.equal(payload.auth, "Bearer re_test");
  assert.equal(payload.html, undefined);
  assert.match(payload.text, /CDR to PNG fails/);
});

test("limits how many one address can send", async () => {
  globalThis.fetch = async () => ({ ok: true });
  const results = [];
  for (let i = 0; i < 6; i++) results.push((await call(good, { ip: "203.0.113.9" })).status);
  assert.deepEqual(results, [200, 200, 200, 200, 429, 429]);
});

test("says so when the email settings are missing", async () => {
  delete process.env.RESEND_API_KEY;
  assert.equal((await call(good, { ip: "203.0.113.10" })).status, 503);
});

test("help requests need an email, and purchase problems stand out in the subject", () => {
  const help = { type: "help", about: "purchase", title: "Upgrade not unlocked", details: "I paid but the tools are locked.", from: "license" };
  assert.match(build(help).error, /email/);
  const { email } = build({ ...help, email: "buyer@example.com", version: "0.5.4-alpha", source: "Microsoft Store" });
  assert.match(email.subject, /^\[OrbitDrop\] PURCHASE help: Upgrade not unlocked$/);
  assert.match(email.text, /About: The upgrade or a purchase/);
  assert.match(email.text, /OrbitDrop version: 0\.5\.4-alpha/);
  assert.match(email.text, /opened from the app \(license\)/);
  assert.equal(email.replyTo, "buyer@example.com");
});

test("an unknown help topic becomes Something else, and from keeps letters only", () => {
  const { email } = build({ type: "help", about: "admin", title: "Question", details: "How do I change the shortcut?", email: "a@example.com", from: "tray<script>" });
  assert.match(email.subject, /Help request/);
  assert.match(email.text, /About: Something else/);
  assert.match(email.text, /\(trayscript\)/);
});

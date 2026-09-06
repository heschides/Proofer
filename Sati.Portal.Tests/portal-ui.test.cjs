// Dependency-free browser-surface checks. Server authorization is tested separately
// by PortalSecurityTests; this harness exercises the actual shipped script.
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const source = fs.readFileSync(path.join(__dirname, "../Sati.Portal/wwwroot/portal.js"), "utf8");
const html = fs.readFileSync(path.join(__dirname, "../Sati.Portal/wwwroot/index.html"), "utf8");
const token = "a".repeat(64);
const flush = async () => { for (let index = 0; index < 8; index++) await new Promise(setImmediate); };

function browser(options = {}) {
  const elements = new Map();
  const calls = [];
  const events = new Map();
  let focused = null;
  const formFields = {
    "auth-form": ["pin"], "consent-form": ["can-access", "accepts-electronic"],
    "sign-form": ["typed-name", "agrees-to-intent"], "decision-form": ["decision", "reason"]
  };
  for (const match of html.matchAll(/<([a-z][\w-]*)\b[^>]*\bid="([^"]+)"[^>]*>/gi)) {
    const node = {
      id: match[2], tagName: match[1].toUpperCase(), hidden: match[0].includes(" hidden"),
      value: "", checked: false, textContent: "", disabled: false, listeners: new Map(),
      focus() { focused = this.id; }, reportValidity() { return true; },
      reset() { for (const id of formFields[this.id] ?? []) { elements.get(id).value = ""; elements.get(id).checked = false; } },
      addEventListener(event, callback) { this.listeners.set(event, callback); }
    };
    Object.defineProperty(node, "innerHTML", { set() { throw new Error("Untrusted HTML must never be evaluated"); } });
    elements.set(node.id, node);
  }
  const state = {
    signerName: "Synthetic Signer", capacity: "Consumer", documentName: "Notice of Privacy Practices",
    disclosureVersion: "synthetic-v1", disclosureText: "Synthetic disclosure. Paper is available.",
    intentText: "I acknowledge receipt only.", state: "Viewed", hasConsent: false,
    documentReleased: false, accessAcknowledged: false,
    sessionBinding: "synthetic-session-a",
    sessionExpiresAtUtc: new Date(Date.now() + 30 * 60000).toISOString(),
    requestExpiresAtUtc: new Date(Date.now() + 3 * 86400000).toISOString(), hasPackage: false,
    ...options.state
  };
  const window = {
    location: { pathname: options.path ?? `/s/${token}`, reload() { window.reloaded = true; } },
    history: { replaceState(_state, _title, address) { window.address = address; } },
    addEventListener(event, callback) { events.set(event, callback); },
    setInterval(callback) { window.tick = callback; }
  };
  const forbiddenStorage = { get() { throw new Error("Private data must not use browser storage"); } };
  Object.defineProperty(window, "localStorage", forbiddenStorage);
  Object.defineProperty(window, "sessionStorage", forbiddenStorage);
  const document = {
    body: { classList: { toggle() {} } },
    getElementById: id => { assert.ok(elements.has(id), `Unknown shipped UI element ${id}`); return elements.get(id); },
    querySelectorAll: query => { assert.equal(query, "button"); return [...elements.values()].filter(node => node.tagName === "BUTTON"); }
  };
  Object.defineProperty(document, "cookie", { get() { throw new Error("JavaScript must not inspect the HttpOnly lease"); }, set() { throw new Error("JavaScript must not write authentication cookies"); } });
  const response = (value, status = 200) => ({ ok: status >= 200 && status < 300, status, async json() { return value; } });
  const fetch = async (url, init) => {
    calls.push({ url, ...init });
    assert.equal(init.credentials, "same-origin"); assert.equal(init.redirect, "error"); assert.equal(init.cache, "no-store");
    if (init.method === "POST") assert.equal(init.headers["X-Sati-CSRF"], "synthetic-csrf");
    if (options.fetch) { const custom = await options.fetch(url, init, state, response); if (custom !== undefined) return custom; }
    if (url === "/portal/bootstrap") return response({ csrfToken: "synthetic-csrf", enabled: options.enabled !== false });
    if (url === "/portal/auth" || url === "/portal/state") return response({ ...state });
    if (url === "/portal/consent") { state.hasConsent = true; return response({ ...state }); }
    if (url === "/portal/sign") { state.state = "Signed"; return response({ ...state }); }
    return response({ complete: true });
  };
  vm.runInNewContext(source, { window, document, fetch, console: { log() { throw new Error("Sensitive actions must not log"); } }, Error, TypeError, Date, Number, JSON }, { timeout: 1000 });
  const emit = (id, event = "click") => { const node = elements.get(id); assert.ok(node.listeners.has(event)); node.listeners.get(event)({ currentTarget: node, preventDefault() {} }); };
  const authenticate = async () => { await flush(); elements.get("pin").value = "73925814"; emit("auth-form", "submit"); await flush(); };
  return { elements, calls, window, state, events, emit, authenticate, response, focused: () => focused };
}

test("the invitation leaves browser history before a request; no document is fetched automatically", async () => {
  const page = browser();
  assert.equal(page.window.address, "/");
  await page.authenticate();
  assert.equal(page.elements.get("pin").value, "");
  assert.equal(page.elements.get("signer-name").textContent, "Synthetic Signer");
  const auth = page.calls.find(call => call.url === "/portal/auth");
  assert.deepEqual(JSON.parse(auth.body), { token, pin: "73925814", receipt: false });
  assert.equal(page.calls.some(call => call.url.includes("document.pdf")), false);
  assert.equal([...page.elements.values()].some(node => node.textContent.includes(token)), false);
});

test("each tab binds actions and PDF downloads to the document it displayed", async () => {
  let cookieSession;
  const sharedCookieServer = (url, init, state, response) => {
    if (url === "/portal/auth") { cookieSession = state.sessionBinding; return response({ ...state }); }
    if (url === "/portal/sign" || url === "/portal/state") {
      if (init.headers["X-Sati-Session"] !== cookieSession) return response({ code: "signature_session_changed", message: "Session unavailable" }, 409);
    }
  };
  const first = browser({ state: { sessionBinding: "displayed-a", hasConsent: true }, fetch: sharedCookieServer });
  await first.authenticate();
  const second = browser({ state: { sessionBinding: "displayed-b", hasConsent: true }, fetch: sharedCookieServer });
  await second.authenticate();
  assert.equal(first.elements.get("document-link").href, "/portal/document.pdf?session=displayed-a");
  assert.equal(second.elements.get("document-link").href, "/portal/document.pdf?session=displayed-b");
  first.elements.get("typed-name").value = "Synthetic Signer";
  first.elements.get("agrees-to-intent").checked = true;
  first.emit("sign-form", "submit"); await flush();
  assert.equal(first.calls.find(call => call.url === "/portal/sign").headers["X-Sati-Session"], "displayed-a");
  assert.equal(first.elements.get("review-panel").hidden, true);
  assert.equal(first.elements.get("typed-name").value, "");
  assert.equal(first.elements.get("agrees-to-intent").checked, false);
  assert.match(first.elements.get("end-heading").textContent, /intended document/);
  assert.equal(first.state.state, "Viewed"); assert.equal(second.state.state, "Viewed");
  second.emit("refresh-state"); await flush();
  assert.equal(second.calls.find(call => call.url === "/portal/state").headers["X-Sati-Session"], "displayed-b");
});

test("a changed session response clears earlier choices before showing another document", async () => {
  const page = browser({ state: { hasConsent: true }, fetch: (url, _init, state, response) =>
    url === "/portal/state" ? response({ ...state, sessionBinding: "another-document" }) : undefined });
  await page.authenticate();
  page.elements.get("typed-name").value = "Synthetic Signer";
  page.elements.get("agrees-to-intent").checked = true;
  page.emit("refresh-state"); await flush();
  assert.equal(page.elements.get("review-panel").hidden, true);
  assert.equal(page.elements.get("typed-name").value, "");
  assert.equal(page.elements.get("agrees-to-intent").checked, false);
  assert.equal(page.elements.get("signer-name").textContent, "");
});

test("successful signing rotates to its receipt session with all signing choices cleared", async () => {
  const page = browser({ state: { hasConsent: true }, fetch: (url, _init, state, response) =>
    url === "/portal/sign" ? response({ ...state, state: "Signed", sessionBinding: "new-receipt-session" }) : undefined });
  await page.authenticate();
  page.elements.get("typed-name").value = "Synthetic Signer";
  page.elements.get("agrees-to-intent").checked = true;
  page.emit("sign-form", "submit"); await flush();
  assert.equal(page.elements.get("review-panel").hidden, false);
  assert.equal(page.elements.get("sign-panel").hidden, true);
  assert.equal(page.elements.get("decision-panel").hidden, true);
  assert.equal(page.elements.get("typed-name").value, "");
  assert.equal(page.elements.get("agrees-to-intent").checked, false);
  assert.equal(page.elements.get("document-link").href, "/portal/document.pdf?session=new-receipt-session");
  page.emit("refresh-state"); await flush();
  assert.equal(page.calls.find(call => call.url === "/portal/state").headers["X-Sati-Session"], "new-receipt-session");
});

test("a failed PIN attempt clears the masked control and reveals no signer data", async () => {
  const page = browser({ fetch: (url, _init, _state, response) => url === "/portal/auth" ? response({ message: "unavailable" }, 404) : undefined });
  await page.authenticate();
  assert.equal(page.elements.get("pin").value, "");
  assert.equal(page.elements.get("review-panel").hidden, true);
  assert.equal(page.elements.get("signer-name").textContent, "");
  assert.match(page.elements.get("status").textContent, /could not open/);
});

test("offline signing preserves typed intent for an explicit status check", async () => {
  const page = browser({ state: { hasConsent: true }, fetch: url => { if (url === "/portal/sign") throw new TypeError("offline"); } });
  await page.authenticate();
  page.elements.get("typed-name").value = "Synthetic Signer";
  page.elements.get("agrees-to-intent").checked = true;
  page.emit("sign-form", "submit"); await flush();
  assert.equal(page.elements.get("typed-name").value, "Synthetic Signer");
  assert.equal(page.elements.get("agrees-to-intent").checked, true);
  assert.match(page.elements.get("status").textContent, /refresh the request status/);
});

test("an authentication response arriving after page exit cannot restore private content", async () => {
  let finish;
  const pending = new Promise(resolve => { finish = resolve; });
  const page = browser({ fetch: url => url === "/portal/auth" ? pending : undefined });
  await flush(); page.elements.get("pin").value = "73925814"; page.emit("auth-form", "submit");
  page.events.get("pagehide")(); finish(page.response({ ...page.state })); await flush();
  assert.equal(page.elements.get("review-panel").hidden, true);
  assert.equal(page.elements.get("signer-name").textContent, "");
  assert.equal(page.elements.get("pin").value, "");
});

test("untrusted text stays text and a receipt has no signing controls", async () => {
  const malicious = '<img src=x onerror="steal()">';
  const page = browser({ path: `/r/${token}`, state: { signerName: malicious, state: "Signed", hasConsent: true, hasPackage: false } });
  await page.authenticate();
  assert.equal(page.elements.get("signer-name").textContent, malicious);
  assert.equal(page.elements.get("sign-panel").hidden, true);
  assert.equal(page.elements.get("consent-panel").hidden, true);
  assert.equal(page.elements.get("decision-panel").hidden, true);
  assert.equal(page.elements.get("document-actions").hidden, true);
  page.state.hasPackage = true; page.emit("refresh-package"); await flush();
  assert.equal(page.elements.get("document-actions").hidden, false);
  assert.equal(JSON.parse(page.calls.find(call => call.url === "/portal/auth").body).receipt, true);
});

test("a disabled portal offers assistance without opening authentication", async () => {
  const page = browser({ enabled: false }); await flush();
  assert.equal(page.elements.get("auth-panel").hidden, true);
  assert.equal(page.elements.get("end-panel").hidden, false);
  assert.match(page.elements.get("end-message").textContent, /paper or assisted/);
});

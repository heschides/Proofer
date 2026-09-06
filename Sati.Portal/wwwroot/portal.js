"use strict";
(() => {
  const element = id => document.getElementById(id);
  const initialPath = /^\/(s|r)\/([a-fA-F0-9]{64})\/?$/.exec(window.location.pathname);
  let invitation = initialPath ? initialPath[2] : null;
  const receiptLink = initialPath?.[1] === "r";
  // The invitation is kept only in this page's memory until authentication succeeds.
  // It must not remain in history, referrers, storage, DOM attributes or diagnostic output.
  window.history.replaceState(null, "", "/");
  let csrf = "";
  let details = null;
  let busy = false;
  let enabled = false;
  let generation = 0;

  function say(message, focus = false) {
    element("status").textContent = message;
    if (focus) element("status").focus();
  }
  function text(id, value) { element(id).textContent = typeof value === "string" ? value : ""; }
  function setBusy(value) {
    busy = value;
    document.body.classList.toggle("working", value);
    document.querySelectorAll("button").forEach(button => { button.disabled = value; });
  }
  function hidePanels() { ["auth-panel", "review-panel", "end-panel"].forEach(id => { element(id).hidden = true; }); }
  function clearPrivateContent() {
    details = null;
    element("document-link").href = "/";
    ["signer-name", "signer-capacity", "document-heading", "disclosure", "intent-text", "disclosure-version"].forEach(id => text(id, ""));
    ["auth-form", "consent-form", "sign-form", "decision-form"].forEach(id => element(id).reset());
    element("session-warning").hidden = true;
  }
  function end(message, heading = "This session is closed.") {
    generation++;
    invitation = null;
    clearPrivateContent();
    hidePanels();
    element("end-panel").hidden = false;
    text("end-heading", heading);
    text("end-message", message);
    say("");
    element("end-heading").focus();
  }
  async function request(path, method = "GET", body) {
    const headers = method === "POST" ? { "Content-Type": "application/json", "X-Sati-CSRF": csrf } : {};
    if (details?.sessionBinding && path !== "/portal/auth" && path !== "/portal/bootstrap")
      headers["X-Sati-Session"] = details.sessionBinding;
    const response = await fetch(path, {
      method, credentials: "same-origin", cache: "no-store", redirect: "error",
      headers,
      body: method === "POST" ? JSON.stringify(body ?? {}) : undefined
    });
    const value = response.status === 204 ? null : await response.json().catch(() => null);
    if (!response.ok) {
      const error = new Error(typeof value?.message === "string" ? value.message : "This action could not be completed. Please try again or contact your case manager.");
      error.status = response.status;
      error.code = value?.code;
      throw error;
    }
    return value;
  }
  async function action(operation, { authenticating = false } = {}) {
    if (busy) return;
    const ticket = generation;
    setBusy(true);
    say("");
    try { await operation(ticket); }
    catch (error) {
      if (ticket !== generation) return;
      if (error.code === "signature_session_changed") {
        end("Another link changed the session in this browser. Reopen the intended document's private link and enter its code before reviewing or signing. Your earlier choices have been cleared.", "Please reopen the intended document.");
      } else if (!authenticating && (error.status === 401 || error.status === 404)) {
        end("This private session is unavailable. Reopen the original link, or contact your case manager for help or a paper copy.", "Your private session is unavailable.");
      } else {
        say(authenticating
          ? "The link and code could not open a session. Check the code or contact your case manager for a new link, assistance, or paper."
          : error instanceof TypeError ? "The connection was interrupted. Your text is still here. Check your connection and refresh the request status before trying again."
          : error.message, true);
      }
    } finally { setBusy(false); }
  }
  function showDetails(value, moveFocus = false, completedHere = false) {
    if (!value || typeof value.signerName !== "string" || typeof value.sessionExpiresAtUtc !== "string" || typeof value.sessionBinding !== "string" || !value.sessionBinding)
      throw new Error("The private session could not be loaded. Refresh or contact your case manager.");
    if (details && details.sessionBinding !== value.sessionBinding) {
      if (completedHere && value.state === "Signed") {
        clearPrivateContent();
      } else {
        const error = new Error("The private session changed.");
        error.code = "signature_session_changed";
        throw error;
      }
    }
    details = value;
    element("document-link").href = "/portal/document.pdf?session=" + encodeURIComponent(value.sessionBinding);
    hidePanels();
    element("review-panel").hidden = false;
    text("document-heading", value.documentName);
    text("signer-name", value.signerName);
    text("signer-capacity", ({ Consumer: "Consumer", Guardian: "Guardian", AuthorizedRepresentative: "Authorized representative" })[value.capacity] ?? value.capacity);
    text("disclosure", value.disclosureText);
    text("disclosure-version", value.disclosureVersion);
    text("intent-text", value.intentText);
    const signed = value.state === "Signed";
    element("consent-panel").hidden = signed || value.hasConsent;
    element("sign-panel").hidden = signed || !value.hasConsent;
    element("decision-panel").hidden = signed;
    element("package-pending").hidden = !signed || value.hasPackage;
    element("refresh-package").hidden = !signed || value.hasPackage;
    element("document-actions").hidden = signed && !value.hasPackage;
    text("review-step", signed ? "Your signed copy and certificate" : "1. Open and keep your document");
    text("document-help", signed ? "Your signature is recorded. Save the signed copy and evidence certificate for your records. You can also request a copy from your case manager." : "Open the complete PDF. Read it at your own pace, and save or print a copy. Opening a document does not sign it.");
    updateClock();
    if (moveFocus) element(signed ? "document-heading" : value.hasConsent ? "sign-heading" : "document-heading").focus();
  }
  async function refresh(ticket, moveFocus = false) {
    const value = await request("/portal/state");
    if (ticket === generation) showDetails(value, moveFocus);
  }
  function updateClock() {
    if (!details) return;
    const remaining = Date.parse(details.sessionExpiresAtUtc) - Date.now();
    if (!Number.isFinite(remaining) || remaining <= 0) {
      end("Use the original private link to review the current request status, or contact your case manager for assistance. Closing this page does not undo an action already sent.", "Your session has ended.");
      return;
    }
    element("session-warning").hidden = remaining > 120000;
    const signed = details.state === "Signed";
    element("extend-session").hidden = signed;
    text("session-warning-text", signed
      ? "This private session ends soon. Save your signed copy now, or use your receipt link again later."
      : "This private session ends in less than two minutes. You may keep it open while you review, up to the link's expiry time.");
  }

  element("auth-form").addEventListener("submit", event => {
    event.preventDefault();
    if (!enabled || !invitation || busy || !event.currentTarget.reportValidity()) return;
    const pin = element("pin").value;
    element("pin").value = "";
    void action(async ticket => {
      try {
        const value = await request("/portal/auth", "POST", { token: invitation, pin, receipt: receiptLink });
        if (ticket !== generation) return;
        invitation = null;
        showDetails(value, true);
      } finally { element("pin").value = ""; }
    }, { authenticating: true });
  });
  element("consent-form").addEventListener("submit", event => {
    event.preventDefault();
    if (!event.currentTarget.reportValidity()) return;
    const canAccessAndRetain = element("can-access").checked;
    const acceptsElectronicRecords = element("accepts-electronic").checked;
    void action(async ticket => {
      await request("/portal/consent", "POST", { canAccessAndRetain, acceptsElectronicRecords });
      if (ticket === generation) await refresh(ticket, true);
    });
  });
  element("sign-form").addEventListener("submit", event => {
    event.preventDefault();
    if (!event.currentTarget.reportValidity()) return;
    const typedName = element("typed-name").value;
    const agreesToIntent = element("agrees-to-intent").checked;
    void action(async ticket => {
      const value = await request("/portal/sign", "POST", { typedName, agreesToIntent });
      if (ticket !== generation) return;
      element("sign-form").reset();
      showDetails(value, true, true);
      say("Your signature was recorded. Keep a copy of the signed document and certificate.", true);
    });
  });
  element("decision-form").addEventListener("submit", event => {
    event.preventDefault();
    if (!event.currentTarget.reportValidity()) return;
    const decision = element("decision").value;
    const reason = element("reason").value;
    void action(async ticket => {
      await request("/portal/decision", "POST", { decision, reason });
      if (ticket !== generation) return;
      end(decision === "changes" ? "Your request for changes was recorded. This document has not been signed. Your case manager can prepare a revised document for a new review."
        : decision === "decline" ? "Your decision not to sign was recorded. This document has not been signed. Contact your case manager if you need to discuss other options."
        : "Your choice to stop electronic signing was recorded. This document has not been signed. Contact your case manager to arrange paper or assistance.", "Your choice was recorded.");
    });
  });
  ["refresh-state", "refresh-package"].forEach(id => element(id).addEventListener("click", () => void action(ticket => refresh(ticket))));
  element("extend-session").addEventListener("click", () => void action(async ticket => {
    const value = await request("/portal/extend", "POST");
    if (ticket !== generation || !details) return;
    details.sessionExpiresAtUtc = value.expiresAtUtc;
    updateClock(); say("The session time was updated. The original link expiry still applies.");
  }));
  element("logout").addEventListener("click", () => void action(async ticket => {
    await request("/portal/logout", "POST");
    if (ticket === generation) end("You have closed this private session. Keep any saved document in a secure place.");
  }));
  window.addEventListener("pagehide", () => { generation++; invitation = null; csrf = ""; clearPrivateContent(); hidePanels(); });
  window.addEventListener("pageshow", event => { if (event.persisted) window.location.reload(); });
  window.setInterval(updateClock, 10000);

  void action(async ticket => {
    const bootstrap = await request("/portal/bootstrap");
    if (ticket !== generation) return;
    csrf = bootstrap?.csrfToken ?? "";
    enabled = bootstrap?.enabled === true;
    if (!enabled || !csrf) {
      end("Electronic signing is not available in this environment. Contact your case manager for a paper or assisted option.", "Signing is unavailable.");
    } else if (invitation) {
      hidePanels(); element("auth-panel").hidden = false; say(""); element("auth-heading").focus();
    } else {
      await refresh(ticket, true); say("");
    }
  });
})();

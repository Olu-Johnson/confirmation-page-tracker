/*!
 * Booking tracker for Zylux Meet + TrackCrest.
 *
 * Add to every page of the client's site (e.g. app/layout of dept.habyyer.com):
 *   <script src="https://YOUR-APP/tracker.js" data-booking-tracker
 *           data-site="dept-habyyer"
 *           data-api="https://YOUR-API"
 *           data-confirm-url="https://YOUR-APP/booking-confirmed" async></script>
 *
 * data-site is the site's key in the admin (Sites), where lead / sale / both and the TrackCrest
 * postback URLs are set. Nothing about what gets reported is decided here.
 *
 * 1. Saves the TrackCrest click ID (and sub1–sub5) from the landing URL on the client's domain.
 * 2. Listens for Zylux's "scheduler:booked" event (inline embed AND popup modal). A paid booking
 *    ("awaiting_payment") is recorded but the visitor stays to pay in the embed; they're moved on
 *    when Zylux sends "scheduler:paid".
 *    Also handles Zylux "Redirect on booking" set to this site, e.g. https://client.com/?bt_booked=1
 *    (Zylux appends uid, startTime, title), so paid bookings in another tab are still recorded.
 * 3. Sends {uid, clickId} to the API, then redirects to the confirmation page.
 * Visitors without a click ID are still redirected but are not recorded.
 *
 * Zylux Meet's embed.js also passes the click ID into the booking itself, so the booking is tracked
 * from Zylux's signed webhooks even if this browser call never happens (closed tab after paying).
 */
(function () {
  "use strict";
  if (window.BookingTracker) return;

  var script = document.currentScript || document.querySelector("script[data-booking-tracker]");
  var ds = (script && script.dataset) || {};
  var list = function (v, d) { return (v || d).split(",").map(function (s) { return s.trim(); }).filter(Boolean); };

  var cfg = {
    api: (ds.api || "").replace(/\/+$/, ""),
    site: ds.site || "",
    confirmUrl: ds.confirmUrl || "",
    clickParams: list(ds.clickParams, "click_id,clickid,cid,tc_cid"),
    affiliateParams: list(ds.affiliateParams, "aff_id,affiliate_id,aff"),
    windowDays: parseInt(ds.windowDays || "30", 10),
    cookieDomain: ds.cookieDomain || ""
  };
  var KEY = "bt_attr";
  var CLICK_RX = /^[A-Za-z0-9_.\-]{4,128}$/;

  function store(data) {
    var json = JSON.stringify(data);
    try { localStorage.setItem(KEY, json); } catch (e) {}
    document.cookie = KEY + "=" + encodeURIComponent(json) + "; Max-Age=" + cfg.windowDays * 86400 +
      "; Path=/; SameSite=Lax" + (cfg.cookieDomain ? "; Domain=" + cfg.cookieDomain : "") +
      (location.protocol === "https:" ? "; Secure" : "");
  }

  function read() {
    var raw = null;
    try { raw = localStorage.getItem(KEY); } catch (e) {}
    if (!raw) {
      var m = document.cookie.match(new RegExp("(?:^|; )" + KEY + "=([^;]*)"));
      if (m) raw = decodeURIComponent(m[1]);
    }
    if (!raw) return null;
    try {
      var d = JSON.parse(raw);
      if (!d || !CLICK_RX.test(d.clickId || "")) return null;
      if (Date.now() - (d.ts || 0) > cfg.windowDays * 86400000) return null;
      return d;
    } catch (e) { return null; }
  }

  // ---- 1. Capture attribution from the landing URL (last click wins) ----
  (function capture() {
    var q = new URLSearchParams(location.search);
    var first = function (names) {
      for (var i = 0; i < names.length; i++) { var v = (q.get(names[i]) || "").trim(); if (v) return v; }
      return "";
    };
    var clickId = first(cfg.clickParams);
    if (!CLICK_RX.test(clickId)) return;
    var data = {
      clickId: clickId,
      affiliateId: first(cfg.affiliateParams).slice(0, 100) || undefined,
      ts: Date.now(),
      landingUrl: location.href.slice(0, 1000),
      referrer: (document.referrer || "").slice(0, 1000) || undefined
    };
    for (var n = 1; n <= 5; n++) { var s = q.get("sub" + n); if (s) data["sub" + n] = s.slice(0, 100); }
    store(data);
  })();

  // ---- 2. On booking: record (if tracked), then redirect ----
  function redirect(b) {
    if (!cfg.confirmUrl || !b.uid) return;
    var url;
    try { url = new URL(cfg.confirmUrl, location.href); } catch (e) { return; }
    url.searchParams.set("uid", b.uid);
    if (b.startTime) url.searchParams.set("startTime", b.startTime);
    if (b.endTime) url.searchParams.set("endTime", b.endTime);
    if (b.title) url.searchParams.set("title", b.title);
    if (b.status) url.searchParams.set("status", b.status);
    location.href = url.toString();
  }

  // stay: record the booking without leaving the page (the visitor still has to pay in the embed).
  function handleBooking(b, stay) {
    if (!b || !b.uid) return;
    var go = stay ? function () {} : redirect;

    var attr = read();
    var sentKey = "bt_sent_" + b.uid;
    var alreadySent = false;
    try { alreadySent = !!sessionStorage.getItem(sentKey); } catch (err) {}

    if (!attr || !cfg.api || alreadySent) { go(b); return; }
    try { sessionStorage.setItem(sentKey, "1"); } catch (err) {}

    var finished = false;
    var finish = function () { if (finished) return; finished = true; go(b); };
    var body = {
      uid: b.uid, clickId: attr.clickId, affiliateId: attr.affiliateId, status: b.status,
      title: b.title, startTime: b.startTime, endTime: b.endTime,
      sub1: attr.sub1, sub2: attr.sub2, sub3: attr.sub3, sub4: attr.sub4, sub5: attr.sub5,
      site: location.origin, siteKey: cfg.site || undefined,
      landingUrl: attr.landingUrl, referrer: attr.referrer, clickedAt: attr.ts
    };
    // text/plain avoids a CORS preflight; keepalive lets it finish while the page navigates.
    fetch(cfg.api + "/api/track/booking", {
      method: "POST", mode: "cors", keepalive: true,
      headers: { "Content-Type": "text/plain" }, body: JSON.stringify(body)
    }).then(finish, finish);
    setTimeout(finish, 1500);
  }

  // a) Embedded scheduler (inline or popup) in this tab.
  window.addEventListener("scheduler:booked", function (e) {
    var b = (e && e.detail) || {};
    handleBooking(b, b.status === "awaiting_payment");
  });
  // A paid booking's payment went through in the embed: now go to the confirmation page.
  window.addEventListener("scheduler:paid", function (e) {
    var b = (e && e.detail) || {};
    b.status = "accepted";
    handleBooking(b, false);
  });

  // b) Zylux "Redirect on booking" pointing back at this site (e.g. https://client.com/?bt_booked=1).
  //    Covers payment in the same tab or a new tab, and the hosted Zylux page: whichever tab lands
  //    here still has the click ID (same-origin storage), records the booking and moves on.
  (function fromRedirect() {
    var q = new URLSearchParams(location.search);
    var uid = (q.get("uid") || "").trim();
    if (!/^[A-Za-z0-9_-]{3,100}$/.test(uid) || !(q.has("bt_booked") || q.has("startTime"))) return;
    handleBooking({
      uid: uid, startTime: q.get("startTime") || "", endTime: q.get("endTime") || "",
      title: q.get("title") || "", status: q.get("status") || ""
    });
  })();

  window.BookingTracker = {
    getAttribution: read,
    clear: function () {
      try { localStorage.removeItem(KEY); } catch (e) {}
      document.cookie = KEY + "=; Max-Age=0; Path=/" + (cfg.cookieDomain ? "; Domain=" + cfg.cookieDomain : "");
    }
  };
})();

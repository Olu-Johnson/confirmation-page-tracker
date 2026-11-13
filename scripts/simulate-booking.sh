#!/usr/bin/env bash
# Simulates tracked bookings for one site without Zylux or TrackCrest:
#   1. free booking, webhook only: BOOKING_CREATED carries tracking.click_id      -> lead
#   2. paid booking, webhook only: BOOKING_PAYMENT_INITIATED, then BOOKING_PAID   -> lead, then sale
#   3. paid booking through the browser tracker (tracker.js call, then webhooks) -> lead + sale
#   4. untracked booking (no click ID anywhere)                                  -> not recorded
# Which of lead / sale is sent depends on the site's mode (Admin → Sites).
#
# Usage: scripts/simulate-booking.sh <site-key> <webhook-secret> [api-url] [origin]
#   site-key / webhook-secret: from Admin → Sites → the site
#   origin: one of the site's allowed origins (for the tracker call), default http://localhost:3000
set -euo pipefail

SITE="${1:?site key, e.g. dept-habyyer}"
SECRET="${2:?webhook secret of the site}"
API="${3:-http://localhost:5145}"
ORIGIN="${4:-http://localhost:3000}"
TS="$(date +%s)"
START="$(date -u -v+3d +%Y-%m-%dT14:00:00.000Z 2>/dev/null || date -u -d '+3 days' +%Y-%m-%dT14:00:00.000Z)"
END="${START/14:00/15:00}"

webhook() { # $1 trigger, $2 payload json
  local body sig
  body="{\"triggerEvent\":\"$1\",\"createdAt\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"payload\":$2}"
  sig="$(printf '%s' "$body" | openssl dgst -sha256 -hmac "$SECRET" | sed 's/^.* //')"
  printf '   %-26s ' "$1"
  curl -sS -X POST "$API/api/webhooks/zylux/$SITE" -H "Content-Type: application/json" \
    -H "X-Scheduler-Trigger: $1" -H "X-Scheduler-Signature-256: $sig" --data "$body"
  echo
}

payload() { # $1 uid, $2 status, $3 payment json or null, $4 tracking json or null
  echo "{\"uid\":\"$1\",\"title\":\"Consultation between Dept Global and Ada\",\"startTime\":\"$START\",\"endTime\":\"$END\",\"status\":\"$2\",\"organizer\":{\"name\":\"Dept Global Solutions\",\"email\":\"owner@example.com\",\"timeZone\":\"America/Toronto\"},\"attendees\":[{\"name\":\"Ada Test\",\"email\":\"ada@example.com\",\"timeZone\":\"Africa/Lagos\"}],\"location\":{\"type\":\"cal-video\",\"url\":\"https://meet.jit.si/sim\"},\"payment\":$3,\"tracking\":$4}"
}

# Zylux Meet amounts are × 100 (5000 = 50.00)
PENDING='{"amount":5000,"currency":"cad","status":"unpaid","provider":"stripe"}'
PAID='{"amount":5000,"currency":"cad","status":"paid","provider":"stripe"}'

# (JSON built in variables first: bash 3.2 mangles escaped quotes inside "$(...)".)
UID1="bk_sim${TS}a"
TRACK1='{"click_id":"tc_sim'"$TS"'a","sub1":"facebook","utm_source":"fb"}'
echo "== 1. free booking, webhook only ($UID1)"
P="$(payload "$UID1" accepted null "$TRACK1")"
webhook BOOKING_CREATED "$P"

UID2="bk_sim${TS}b"
TRACK2='{"click_id":"tc_sim'"$TS"'b","aff_id":"aff42"}'
echo "== 2. paid booking, webhook only ($UID2)"
P="$(payload "$UID2" awaiting_payment "$PENDING" "$TRACK2")"
webhook BOOKING_PAYMENT_INITIATED "$P"
P="$(payload "$UID2" accepted "$PAID" "$TRACK2")"
webhook BOOKING_PAID "$P"

UID3="bk_sim${TS}c"; CLICK3="tc_sim${TS}c"
echo "== 3. paid booking through tracker.js ($UID3)"
printf '   %-26s ' "tracker call"
curl -sS -X POST "$API/api/track/booking" -H "Content-Type: text/plain" -H "Origin: $ORIGIN" \
  --data "{\"uid\":\"$UID3\",\"clickId\":\"$CLICK3\",\"siteKey\":\"$SITE\",\"status\":\"awaiting_payment\",\"title\":\"Consultation\",\"startTime\":\"$START\",\"endTime\":\"$END\",\"sub1\":\"google\",\"site\":\"$ORIGIN\",\"landingUrl\":\"$ORIGIN/?click_id=$CLICK3&sub1=google\"}"
echo
P="$(payload "$UID3" accepted "$PAID" null)"
webhook BOOKING_PAID "$P"

echo "== 4. untracked booking (should not be recorded)"
P="$(payload "bk_untracked$TS" accepted null null)"
webhook BOOKING_CREATED "$P"

echo "Done. Open the admin and search for bk_sim$TS"

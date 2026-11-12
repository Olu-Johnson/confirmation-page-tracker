#!/usr/bin/env bash
# Simulates a full tracked booking without Zylux or TrackCrest:
#   1. tracker.js call (browser, after "scheduler:booked")
#   2. signed BOOKING_CREATED webhook (awaiting payment)
#   3. signed BOOKING_PAID webhook  -> conversion is queued and sent to TrackCrest:PostbackUrl
#
# Usage: scripts/simulate-booking.sh [api-url] [webhook-secret]
set -euo pipefail

API="${1:-http://localhost:5145}"
SECRET="${2:-d4c3a738363ee740b872bb7a4cf04f3b8684af3f2bd3a1be}"
UID_="bk_sim$(date +%s)"
CLICK="tc_sim$(date +%s)"
START="$(date -u -v+3d +%Y-%m-%dT14:00:00.000Z 2>/dev/null || date -u -d '+3 days' +%Y-%m-%dT14:00:00.000Z)"
END="${START/14:00/15:00}"

webhook() { # $1 trigger, $2 json body
  local sig
  sig="$(printf '%s' "$2" | openssl dgst -sha256 -hmac "$SECRET" | sed 's/^.* //')"
  curl -sS -X POST "$API/api/webhooks/zylux" -H "Content-Type: application/json" \
    -H "X-Scheduler-Trigger: $1" -H "X-Scheduler-Signature-256: $sig" --data "$2"
  echo
}

echo "== 1. tracker call ($UID_, click $CLICK)"
curl -sS -X POST "$API/api/track/booking" -H "Content-Type: text/plain" -H "Origin: http://localhost:3000" \
  --data "{\"uid\":\"$UID_\",\"clickId\":\"$CLICK\",\"status\":\"awaiting_payment\",\"title\":\"Consultation between Dept Global and Ada\",\"startTime\":\"$START\",\"endTime\":\"$END\",\"sub1\":\"facebook\",\"site\":\"https://dept.habyyer.com\",\"landingUrl\":\"https://dept.habyyer.com/?click_id=$CLICK&sub1=facebook\"}"
echo

PAYLOAD="{\"uid\":\"$UID_\",\"title\":\"Consultation between Dept Global and Ada\",\"startTime\":\"$START\",\"endTime\":\"$END\",\"status\":\"awaiting_payment\",\"organizer\":{\"name\":\"Dept Global Solutions\",\"email\":\"owner@example.com\",\"timeZone\":\"America/Toronto\"},\"attendees\":[{\"name\":\"Ada Test\",\"email\":\"ada@example.com\",\"timeZone\":\"Africa/Lagos\"}],\"location\":{\"type\":\"google-meet\",\"url\":\"https://meet.google.com/abc-defg-hij\"},\"payment\":{\"amount\":50,\"currency\":\"cad\",\"status\":\"pending\",\"provider\":\"stripe\"}}"

echo "== 2. BOOKING_CREATED webhook"
webhook BOOKING_CREATED "{\"triggerEvent\":\"BOOKING_CREATED\",\"createdAt\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"payload\":$PAYLOAD}"

echo "== 3. BOOKING_PAID webhook"
webhook BOOKING_PAID "{\"triggerEvent\":\"BOOKING_PAID\",\"createdAt\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"payload\":${PAYLOAD/\"status\":\"pending\"/\"status\":\"paid\"}}"

echo "== 4. untracked booking (should not be recorded)"
webhook BOOKING_CREATED "{\"triggerEvent\":\"BOOKING_CREATED\",\"payload\":{\"uid\":\"bk_untracked$(date +%s)\",\"status\":\"accepted\"}}"

echo "Done. Open the admin and search for $UID_"

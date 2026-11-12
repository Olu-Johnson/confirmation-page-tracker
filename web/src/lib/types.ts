export type BookingStatus = "Pending" | "AwaitingPayment" | "Accepted" | "Paid" | "Cancelled" | "Rejected";
export type ConversionStatus = "Waiting" | "Queued" | "Sent" | "Failed" | "NotEligible";
export type ReconciliationStatus = "Unreviewed" | "Confirmed" | "Disputed";

export interface TrackedBooking {
  id: string;
  bookingUid: string;
  originalBookingUid: string | null;
  clickId: string;
  affiliateId: string | null;
  sub1: string | null;
  sub2: string | null;
  sub3: string | null;
  sub4: string | null;
  sub5: string | null;
  site: string | null;
  landingUrl: string | null;
  referrer: string | null;
  clickedAt: string | null;
  title: string | null;
  startTime: string | null;
  endTime: string | null;
  status: BookingStatus;
  webhookConfirmed: boolean;
  noShow: boolean | null;
  eventTypeId: string | null;
  attendeeName: string | null;
  attendeeEmail: string | null;
  attendeeTimeZone: string | null;
  organizerName: string | null;
  locationType: string | null;
  locationUrl: string | null;
  paymentRequired: boolean;
  amount: number | null;
  currency: string | null;
  paymentStatus: string | null;
  paymentProvider: string | null;
  paidAt: string | null;
  conversionStatus: ConversionStatus;
  conversionAttemptCount: number;
  nextConversionAttemptAt: string | null;
  conversionSentAt: string | null;
  lastConversionError: string | null;
  reconciliationStatus: ReconciliationStatus;
  reconciliationNotes: string | null;
  reconciledAt: string | null;
  createdAt: string;
  updatedAt: string;
  events: WebhookEvent[];
  attempts: ConversionAttempt[];
}

export interface WebhookEvent {
  id: string;
  trigger: string;
  bookingUid: string | null;
  payload: string;
  receivedAt: string;
}

export interface ConversionAttempt {
  id: string;
  attemptedAt: string;
  url: string;
  httpStatus: number | null;
  success: boolean;
  response: string | null;
  error: string | null;
  manual: boolean;
}

export interface BookingPage {
  items: TrackedBooking[];
  total: number;
  page: number;
  pageSize: number;
}

export interface Stats {
  total: number;
  byStatus: Partial<Record<BookingStatus, number>>;
  byConversion: Partial<Record<ConversionStatus, number>>;
  byReconciliation: Partial<Record<ReconciliationStatus, number>>;
  revenue: { currency: string; amount: number }[];
  sources: { source: string; bookings: number; paid: number; conversionsSent: number }[];
  cancelledAfterSent: number;
  unverified: number;
}

export interface ApiConfig {
  postbackConfigured: boolean;
  postbackHost: string | null;
  webhookSecretConfigured: boolean;
  allowedOrigins: string[];
  requireWebhookConfirmation: boolean;
  lastWebhookAt: string | null;
  lastTrackedAt: string | null;
  lastConversionSentAt: string | null;
}

export const FILTER_KEYS = ["search", "status", "conversion", "reconciliation", "verified", "from", "to"] as const;

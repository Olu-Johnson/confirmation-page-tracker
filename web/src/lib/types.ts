export type BookingStatus = "Pending" | "AwaitingPayment" | "Accepted" | "Paid" | "Cancelled" | "Rejected";
export type ConversionStatus = "Waiting" | "Queued" | "Sent" | "Failed" | "NotEligible";
export type ReconciliationStatus = "Unreviewed" | "Confirmed" | "Disputed";
export type ConversionKind = "Lead" | "Sale";
export type TrackMode = "Lead" | "Sale" | "Both";
export type AffiliatePaymentStatus = "Pending" | "Paid" | "Rejected";

/** One postback to TrackCrest for a booking: its lead or its sale. */
export interface Conversion {
  id: string;
  kind: ConversionKind;
  status: ConversionStatus;
  attemptCount: number;
  nextAttemptAt: string | null;
  sentAt: string | null;
  lastError: string | null;
  createdAt: string;
}

/** A client website promoted through TrackCrest (Admin → Sites). */
export interface Site {
  id: string;
  key: string;
  name: string;
  active: boolean;
  mode: TrackMode;
  leadPostbackUrl: string | null;
  salePostbackUrl: string | null;
  allowedOrigins: string[];
  webhookSecretSet: boolean;
  /** Only returned for a single site. */
  webhookSecret: string | null;
  /** Booking confirmation page theme (lib/themes.ts). */
  confirmationTheme: string;
  createdAt: string;
  updatedAt: string;
}

export interface TrackedBooking {
  id: string;
  bookingUid: string;
  originalBookingUid: string | null;
  siteId: string | null;
  clickId: string;
  affiliateId: string | null;
  sub1: string | null;
  sub2: string | null;
  sub3: string | null;
  sub4: string | null;
  sub5: string | null;
  site: string | null;
  /** Every campaign/affiliate parameter Zylux Meet recorded on the booking. */
  tracking: Record<string, string> | null;
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
  /** Summary of the booking's conversions (the state that needs attention first). */
  conversionStatus: ConversionStatus;
  conversionSentAt: string | null;
  reconciliationStatus: ReconciliationStatus;
  reconciliationNotes: string | null;
  reconciledAt: string | null;
  /** Whether the affiliate was paid for this booking (super admin sets it). */
  affiliatePaymentStatus: AffiliatePaymentStatus;
  /** When the affiliate payment was set to Paid or Rejected. */
  affiliatePaymentDate: string | null;
  createdAt: string;
  updatedAt: string;
  events: WebhookEvent[];
  conversions: Conversion[];
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
  kind: ConversionKind | null;
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
  byAffiliatePayment: Partial<Record<AffiliatePaymentStatus, number>>;
  revenue: { currency: string; amount: number }[];
  sources: { source: string; bookings: number; paid: number; conversionsSent: number }[];
  leads: Partial<Record<ConversionStatus, number>>;
  sales: Partial<Record<ConversionStatus, number>>;
  cancelledAfterSent: number;
  unverified: number;
}

export interface SiteStatus {
  id: string;
  key: string;
  name: string;
  active: boolean;
  mode: TrackMode;
  allowedOrigins: string[];
  leadPostbackConfigured: boolean;
  salePostbackConfigured: boolean;
  webhookSecretConfigured: boolean;
  lastWebhookAt: string | null;
  lastTrackedAt: string | null;
}

export interface ApiConfig {
  requireWebhookConfirmation: boolean;
  lastWebhookAt: string | null;
  lastTrackedAt: string | null;
  lastConversionSentAt: string | null;
  sites: SiteStatus[];
}

export const FILTER_KEYS = ["search", "site", "status", "conversion", "reconciliation", "affiliatePayment", "verified", "from", "to"] as const;

/** A login that sees one site's bookings (Admin → Sites → Users). */
export interface SiteUser {
  id: string;
  siteId: string;
  email: string;
  name: string;
  active: boolean;
  lastLoginAt: string | null;
  createdAt: string;
}

export interface SiteUserInput {
  email?: string;
  name?: string;
  password?: string;
  active?: boolean;
}

export interface SiteInput {
  key?: string;
  name?: string;
  mode?: TrackMode;
  leadPostbackUrl?: string;
  salePostbackUrl?: string;
  allowedOrigins?: string[];
  webhookSecret?: string;
  regenerateSecret?: boolean;
  active?: boolean;
  confirmationTheme?: string;
}

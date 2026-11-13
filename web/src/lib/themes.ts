// Booking confirmation page themes. Keys must match ConfirmationThemes.All in the API (Models/Entities.cs).
// The look itself is in app/booking-confirmed/confirm.module.css ([data-theme="…"]).

export type ThemeKey = "aurora" | "minimal" | "ocean" | "sunset" | "noir";

export interface ConfirmationTheme {
  key: ThemeKey;
  name: string;
  description: string;
  /** Background, card and accent: drawn as a small preview in the admin. */
  swatch: [string, string, string];
  /** Confetti colours on a confirmed booking; null for none. */
  confetti: string[] | null;
}

export const DEFAULT_THEME: ThemeKey = "aurora";

export const THEMES: ConfirmationTheme[] = [
  {
    key: "aurora", name: "Aurora", description: "Deep purple glow, white card and confetti. The original look.",
    swatch: ["#1e1b4b", "#ffffff", "#5b3fd6"], confetti: ["#5b3fd6", "#f59e0b", "#10b981", "#ec4899", "#3b82f6", "#facc15"],
  },
  {
    key: "minimal", name: "Minimal", description: "Plain light page, black type, no confetti. Calm and professional.",
    swatch: ["#f4f4f5", "#ffffff", "#18181b"], confetti: null,
  },
  {
    key: "ocean", name: "Ocean", description: "Blue-to-teal gradient with a soft, airy card.",
    swatch: ["#0e7490", "#f0f9ff", "#0284c7"], confetti: ["#0284c7", "#06b6d4", "#22d3ee", "#a5f3fc", "#ffffff"],
  },
  {
    key: "sunset", name: "Sunset", description: "Warm orange and pink, rounded and friendly.",
    swatch: ["#f97316", "#fffaf5", "#e11d48"], confetti: ["#f97316", "#fb7185", "#e11d48", "#fbbf24", "#fde68a"],
  },
  {
    key: "noir", name: "Noir", description: "Black and gold with serif headings. Premium and understated.",
    swatch: ["#0a0a0a", "#141414", "#d4af37"], confetti: ["#d4af37", "#f5e7a1", "#b8860b"],
  },
];

export function getTheme(key: string | null | undefined): ConfirmationTheme {
  return THEMES.find((t) => t.key === key) ?? THEMES[0];
}

import * as amplitude from '@amplitude/analytics-browser';

const apiKey = import.meta.env.VITE_AMPLITUDE_API_KEY;
let enabled = false;

// defaultTracking is off on purpose: Amplitude's autocapture would track clicks/sessions/
// attribution beyond the scoped "one Page Viewed event per route change" this app tracks
// itself (see App.tsx) — see docs/superpowers/specs/2026-08-23-amplitude-app-tracking-design.md.
export function init(): void {
  if (!apiKey) return;
  amplitude.init(apiKey, { defaultTracking: false });
  enabled = true;
}

export function identify(userId: string): void {
  if (!enabled) return;
  amplitude.setUserId(userId);
}

export function reset(): void {
  if (!enabled) return;
  amplitude.reset();
}

export function trackPageView(path: string): void {
  if (!enabled) return;
  amplitude.track('Page Viewed', { path });
}

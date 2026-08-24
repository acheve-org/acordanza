import * as amplitude from '@amplitude/analytics-browser';

const apiKey = import.meta.env.VITE_AMPLITUDE_API_KEY;
let enabled = false;

// Events go to our own origin, not api2.amplitude.com: that hostname is on every common
// ad-blocker list, so a direct request is simply dropped for a good share of visitors. This
// path is proxied through to Amplitude by the frontend's own nginx (see the `location = /_i/e`
// block in frontend/nginx.conf), which makes the request first-party and unblockable. Kept
// relative so it resolves against whatever origin serves the app — dev, preview and prod all
// work with no extra config. The path is deliberately opaque: filter lists also match on
// tell-tale substrings ("amplitude", "analytics", "track", "collect", "event") even on a
// first-party domain, so it must not contain one.
const PROXY_PATH = '/_i/e';

// defaultTracking is off on purpose: Amplitude's autocapture would track clicks/sessions/
// attribution beyond the scoped "one Page Viewed event per route change" this app tracks
// itself (see App.tsx) — see docs/superpowers/specs/2026-08-23-amplitude-app-tracking-design.md.
//
// Remote config and diagnostics are off for a different reason: both default to true and
// both call Amplitude hosts *other* than the ingestion one (sr-client-cfg.amplitude.com,
// diagnostics.prod.*), which the CSP has never allowed and no proxy here covers — so they
// were only ever producing blocked requests and console noise. Turning them off is what
// lets connect-src stay 'self' with nothing left over.
export function init(): void {
  if (!apiKey) return;
  amplitude.init(apiKey, {
    defaultTracking: false,
    serverUrl: PROXY_PATH,
    remoteConfig: { fetchRemoteConfig: false },
    enableDiagnostics: false,
  });
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

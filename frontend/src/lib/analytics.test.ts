import { beforeEach, describe, expect, it, vi } from 'vitest';

const { initMock, setUserIdMock, resetMock, trackMock } = vi.hoisted(() => ({
  initMock: vi.fn(),
  setUserIdMock: vi.fn(),
  resetMock: vi.fn(),
  trackMock: vi.fn(),
}));

vi.mock('@amplitude/analytics-browser', () => ({
  init: initMock,
  setUserId: setUserIdMock,
  reset: resetMock,
  track: trackMock,
}));

describe('lib/analytics', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  it('does nothing when no API key is configured (local dev)', async () => {
    vi.stubEnv('VITE_AMPLITUDE_API_KEY', '');
    const analytics = await import('./analytics');

    analytics.init();
    analytics.identify('user-1');
    analytics.trackPageView('/routes');
    analytics.reset();

    expect(initMock).not.toHaveBeenCalled();
    expect(setUserIdMock).not.toHaveBeenCalled();
    expect(trackMock).not.toHaveBeenCalled();
    expect(resetMock).not.toHaveBeenCalled();
  });

  it('initializes and forwards every call when an API key is configured', async () => {
    vi.stubEnv('VITE_AMPLITUDE_API_KEY', 'test-key');
    const analytics = await import('./analytics');

    analytics.init();
    analytics.identify('user-1');
    analytics.trackPageView('/routes');
    analytics.reset();

    expect(initMock).toHaveBeenCalledWith('test-key', expect.objectContaining({ defaultTracking: false }));
    expect(setUserIdMock).toHaveBeenCalledWith('user-1');
    expect(trackMock).toHaveBeenCalledWith('Page Viewed', { path: '/routes' });
    expect(resetMock).toHaveBeenCalledOnce();
  });

  // The events go to our own nginx (frontend/nginx.conf's `location = /_i/e`), which
  // forwards them to Amplitude — a request to api2.amplitude.com is blocked by every
  // common ad-blocker, a same-origin one is not. Relative on purpose: it resolves against
  // whatever origin the app is served from, so dev, preview and prod need no config.
  it('sends events to the same-origin proxy path rather than to Amplitude directly', async () => {
    vi.stubEnv('VITE_AMPLITUDE_API_KEY', 'test-key');
    const analytics = await import('./analytics');

    analytics.init();

    expect(initMock).toHaveBeenCalledWith('test-key', expect.objectContaining({ serverUrl: '/_i/e' }));
  });

  // Both default to true and call Amplitude hosts the proxy doesn't cover and the CSP
  // doesn't allow, so leaving them on would mean blocked requests on every page load.
  it('disables the SDK calls that would reach un-proxied Amplitude hosts', async () => {
    vi.stubEnv('VITE_AMPLITUDE_API_KEY', 'test-key');
    const analytics = await import('./analytics');

    analytics.init();

    expect(initMock).toHaveBeenCalledWith(
      'test-key',
      expect.objectContaining({
        remoteConfig: { fetchRemoteConfig: false },
        enableDiagnostics: false,
      }),
    );
  });

  it('stays disabled if identify/trackPageView/reset are called before init', async () => {
    vi.stubEnv('VITE_AMPLITUDE_API_KEY', 'test-key');
    const analytics = await import('./analytics');

    analytics.identify('user-1');

    expect(setUserIdMock).not.toHaveBeenCalled();
  });
});

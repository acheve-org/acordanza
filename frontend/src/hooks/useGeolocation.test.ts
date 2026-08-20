import { act, renderHook, waitFor } from '@testing-library/react';
import { useGeolocation } from './useGeolocation';

function mockGeolocation(impl: (success: PositionCallback, error: PositionErrorCallback) => void) {
  Object.defineProperty(navigator, 'geolocation', {
    configurable: true,
    value: { getCurrentPosition: vi.fn(impl) },
  });
}

const samplePosition = {
  coords: { latitude: 42.1, longitude: -3.4 },
} as GeolocationPosition;

describe('useGeolocation', () => {
  afterEach(() => {
    // @ts-expect-error cleaning up the per-test mock
    delete navigator.geolocation;
  });

  it('auto-requests on mount and transitions to granted with the resolved position', async () => {
    mockGeolocation((success) => success(samplePosition));

    const { result } = renderHook(() => useGeolocation());

    await waitFor(() => expect(result.current.status).toBe('granted'));
    expect(result.current.position).toEqual(samplePosition);
  });

  it('transitions to denied when the browser rejects the request', async () => {
    mockGeolocation((_success, error) => error({ code: 1 } as GeolocationPositionError));

    const { result } = renderHook(() => useGeolocation());

    await waitFor(() => expect(result.current.status).toBe('denied'));
  });

  it('reports unsupported when the browser has no geolocation API', async () => {
    // @ts-expect-error simulating an unsupported browser
    delete navigator.geolocation;

    const { result } = renderHook(() => useGeolocation());

    await waitFor(() => expect(result.current.status).toBe('unsupported'));
  });

  it('re-invokes the browser API on request()', async () => {
    const getCurrentPosition = vi.fn((success: PositionCallback) => success(samplePosition));
    mockGeolocation(getCurrentPosition);

    const { result } = renderHook(() => useGeolocation());
    await waitFor(() => expect(result.current.status).toBe('granted'));
    expect(getCurrentPosition).toHaveBeenCalledTimes(1);

    act(() => {
      result.current.request();
    });

    await waitFor(() => expect(getCurrentPosition).toHaveBeenCalledTimes(2));
  });
});

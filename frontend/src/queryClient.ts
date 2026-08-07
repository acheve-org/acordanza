import { QueryClient } from '@tanstack/react-query';

// staleTime: 0 matches the app's existing "never cache beyond the current
// render" discipline; refetchOnWindowFocus is off to avoid surprising
// refetches until we've lived with the library a bit.
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 0,
      refetchOnWindowFocus: false,
    },
  },
});

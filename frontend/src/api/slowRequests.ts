// Counts API requests still waiting after a few seconds. On the free host the first request after idle wakes the
// server, which can take up to a minute; the shell uses this to say so instead of looking frozen.

const SLOW_AFTER_MS = 3000;

let slowCount = 0;
const subscribers = new Set<() => void>();
const emit = () => subscribers.forEach((fn) => fn());

// Call when a request starts; call the returned function when it settles.
export function trackRequest(): () => void {
  let slow = false;
  const timer = setTimeout(() => {
    slow = true;
    slowCount++;
    emit();
  }, SLOW_AFTER_MS);
  return () => {
    clearTimeout(timer);
    if (!slow) return;
    slow = false;
    slowCount--;
    emit();
  };
}

export function subscribeSlowRequests(fn: () => void) {
  subscribers.add(fn);
  return () => {
    subscribers.delete(fn);
  };
}

export const hasSlowRequests = () => slowCount > 0;

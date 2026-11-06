import { NavLink } from 'react-router-dom';
import { useMe } from '../api/hooks';

// "Sara Khan" → "SK", "Sara" → "S", no name → the email's first letter.
export function initials(name: string, email: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  const first = (s: string) => Array.from(s)[0] ?? '';
  if (words.length === 0) return first(email).toUpperCase() || '?';
  if (words.length === 1) return first(words[0]).toUpperCase();
  return (first(words[0]) + first(words[words.length - 1])).toUpperCase();
}

export function Avatar({ name, email, large = false }: { name: string; email: string; large?: boolean }) {
  return (
    <span className={`avatar ${large ? 'avatar-lg' : ''}`} aria-hidden="true">
      {initials(name, email)}
    </span>
  );
}

// The round initials in the top bar; opens the profile.
export function ProfileLink() {
  const me = useMe();
  return (
    <NavLink to="/profile" className="avatar-link" aria-label="Your profile" title="Your profile">
      <Avatar name={me.data?.displayName ?? ''} email={me.data?.email ?? ''} />
    </NavLink>
  );
}

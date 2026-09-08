/**
 * Display formatting for the Indian enterprise market.
 *
 * Timestamps arrive from the API in UTC and are rendered in the tenant's timezone, which
 * defaults to IST. Dates are day-first, which is what Indian business correspondence uses, and
 * currency groups in lakhs and crores.
 */

const DEFAULT_TIME_ZONE = 'Asia/Kolkata';
const DEFAULT_LOCALE = 'en-IN';

/** Set once from the signed-in user's profile so every screen agrees on the rendering. */
let displayTimeZone = DEFAULT_TIME_ZONE;
let displayLocale = DEFAULT_LOCALE;

/**
 * Applies the tenant's localisation to every subsequent format call.
 *
 * The server stores Windows timezone identifiers; browsers only understand IANA ones, so the
 * few that customers actually use are mapped here and anything unrecognised falls back to IST
 * rather than throwing in the middle of a render.
 */
export function configureFormatting(timeZoneId?: string, locale?: string): void {
  displayTimeZone = toIanaTimeZone(timeZoneId);
  displayLocale = locale || DEFAULT_LOCALE;
}

const WINDOWS_TO_IANA: Record<string, string> = {
  'India Standard Time': 'Asia/Kolkata',
  'UTC': 'UTC',
  'GMT Standard Time': 'Europe/London',
  'Singapore Standard Time': 'Asia/Singapore',
  'Arabian Standard Time': 'Asia/Dubai',
  'Eastern Standard Time': 'America/New_York',
  'Pacific Standard Time': 'America/Los_Angeles',
  'AUS Eastern Standard Time': 'Australia/Sydney',
};

function toIanaTimeZone(timeZoneId?: string): string {
  if (!timeZoneId) {
    return DEFAULT_TIME_ZONE;
  }

  const mapped = WINDOWS_TO_IANA[timeZoneId] ?? timeZoneId;

  // A timezone the browser does not know would make every Intl call throw.
  try {
    new Intl.DateTimeFormat('en', { timeZone: mapped }).format(new Date());
    return mapped;
  } catch {
    return DEFAULT_TIME_ZONE;
  }
}

function parse(value: string | Date | null | undefined): Date | null {
  if (!value) {
    return null;
  }

  const date = value instanceof Date ? value : new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}

/** e.g. `07/09/2026`. */
export function formatDate(value: string | Date | null | undefined): string {
  const date = parse(value);
  if (!date) {
    return '—';
  }

  return new Intl.DateTimeFormat(displayLocale, {
    timeZone: displayTimeZone,
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  }).format(date);
}

/** e.g. `07/09/2026, 14:32`. */
export function formatDateTime(value: string | Date | null | undefined): string {
  const date = parse(value);
  if (!date) {
    return '—';
  }

  return new Intl.DateTimeFormat(displayLocale, {
    timeZone: displayTimeZone,
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(date);
}

/** e.g. `7 Sep 2026, 14:32`. Used where the month name reads better than a number. */
export function formatLongDateTime(value: string | Date | null | undefined): string {
  const date = parse(value);
  if (!date) {
    return '—';
  }

  return new Intl.DateTimeFormat(displayLocale, {
    timeZone: displayTimeZone,
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(date);
}

/** e.g. `14:32`. */
export function formatTime(value: string | Date | null | undefined): string {
  const date = parse(value);
  if (!date) {
    return '—';
  }

  return new Intl.DateTimeFormat(displayLocale, {
    timeZone: displayTimeZone,
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(date);
}

/**
 * A relative description such as `2 hours ago` or `in 15 minutes`.
 *
 * Agents scan queues by recency far more than by absolute time, so this is what a list shows;
 * the exact timestamp is available on hover.
 */
export function formatRelative(value: string | Date | null | undefined): string {
  const date = parse(value);
  if (!date) {
    return '—';
  }

  const formatter = new Intl.RelativeTimeFormat(displayLocale, { numeric: 'auto' });
  const deltaSeconds = (date.getTime() - Date.now()) / 1000;
  const absolute = Math.abs(deltaSeconds);

  if (absolute < 45) {
    return 'just now';
  }

  const units: [Intl.RelativeTimeFormatUnit, number][] = [
    ['year', 31_536_000],
    ['month', 2_592_000],
    ['week', 604_800],
    ['day', 86_400],
    ['hour', 3_600],
    ['minute', 60],
  ];

  for (const [unit, seconds] of units) {
    if (absolute >= seconds) {
      return formatter.format(Math.round(deltaSeconds / seconds), unit);
    }
  }

  return formatter.format(Math.round(deltaSeconds), 'second');
}

/**
 * A duration in minutes rendered compactly: `3h 20m`, `2d 4h`.
 *
 * SLA figures are the main consumer, so a negative value is rendered as an overrun rather than
 * with a minus sign - `2h 15m over` reads correctly to an agent, `-135m` does not.
 */
export function formatDuration(minutes: number | null | undefined): string {
  if (minutes === null || minutes === undefined) {
    return '—';
  }

  const overrun = minutes < 0;
  const total = Math.abs(Math.round(minutes));

  if (total === 0) {
    return 'now';
  }

  const days = Math.floor(total / 1440);
  const hours = Math.floor((total % 1440) / 60);
  const mins = total % 60;

  const parts: string[] = [];
  if (days > 0) {
    parts.push(`${days}d`);
  }
  if (hours > 0) {
    parts.push(`${hours}h`);
  }
  if (mins > 0 && days === 0) {
    parts.push(`${mins}m`);
  }

  const rendered = parts.length > 0 ? parts.join(' ') : `${total}m`;
  return overrun ? `${rendered} over` : rendered;
}

/** Indian numbering: `₹1,23,45,678.00`. */
export function formatCurrency(amount: number | null | undefined, currency = 'INR'): string {
  if (amount === null || amount === undefined) {
    return '—';
  }

  return new Intl.NumberFormat(displayLocale, {
    style: 'currency',
    currency,
    maximumFractionDigits: 2,
  }).format(amount);
}

/** Thousands grouped in the Indian system: `12,34,567`. */
export function formatNumber(value: number | null | undefined): string {
  if (value === null || value === undefined) {
    return '—';
  }

  return new Intl.NumberFormat(displayLocale).format(value);
}

/** Initials for an avatar, at most two letters. */
export function initials(name: string | null | undefined): string {
  if (!name) {
    return '?';
  }

  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) {
    return '?';
  }

  if (parts.length === 1) {
    return parts[0].slice(0, 2).toUpperCase();
  }

  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}

/** Splits a PascalCase enum name into words: `InProgress` becomes `In progress`. */
export function humanise(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }

  const spaced = value.replace(/([a-z0-9])([A-Z])/g, '$1 $2');
  return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase();
}

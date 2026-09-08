import { beforeEach, describe, expect, it } from 'vitest';
import {
  configureFormatting,
  formatCurrency,
  formatDate,
  formatDateTime,
  formatDuration,
  formatNumber,
  humanise,
  initials,
} from './format';

/**
 * Display formatting for the Indian market.
 *
 * These matter more than they look: the API sends UTC, and the difference between rendering
 * 03:30 UTC as "09:00" and as "03:30" is the difference between an agent believing an SLA is
 * due this morning or five and a half hours earlier.
 */
describe('formatting', () => {
  beforeEach(() => {
    configureFormatting('India Standard Time', 'en-IN');
  });

  describe('dates and times', () => {
    it('renders a UTC instant in Indian Standard Time', () => {
      // 03:30 UTC is 09:00 IST.
      expect(formatDateTime('2026-09-07T03:30:00Z')).toBe('07/09/2026, 09:00');
    });

    it('uses the day-first format Indian business correspondence expects', () => {
      // The seventh of September, not the ninth of July.
      expect(formatDate('2026-09-07T12:00:00Z')).toBe('07/09/2026');
    });

    it('rolls the date over when the timezone offset crosses midnight', () => {
      // 20:00 UTC on the sixth is 01:30 IST on the seventh.
      expect(formatDateTime('2026-09-06T20:00:00Z')).toBe('07/09/2026, 01:30');
    });

    it('accepts a Windows timezone identifier as well as an IANA one', () => {
      configureFormatting('Asia/Kolkata', 'en-IN');
      expect(formatDateTime('2026-09-07T03:30:00Z')).toBe('07/09/2026, 09:00');
    });

    it('falls back to IST when the timezone is unknown to the browser', () => {
      // A tenant carrying an identifier this browser does not know must not make every
      // timestamp on the page throw.
      configureFormatting('Mars/Olympus_Mons', 'en-IN');
      expect(formatDateTime('2026-09-07T03:30:00Z')).toBe('07/09/2026, 09:00');
    });

    it('renders an em dash rather than "Invalid Date" for missing values', () => {
      expect(formatDate(null)).toBe('—');
      expect(formatDate(undefined)).toBe('—');
      expect(formatDate('')).toBe('—');
      expect(formatDateTime('not a date')).toBe('—');
    });
  });

  describe('durations', () => {
    it('renders hours and minutes compactly', () => {
      expect(formatDuration(200)).toBe('3h 20m');
      expect(formatDuration(45)).toBe('45m');
      expect(formatDuration(60)).toBe('1h');
    });

    it('drops minutes once a duration spans days', () => {
      expect(formatDuration(1440)).toBe('1d');
      expect(formatDuration(1500)).toBe('1d 1h');
    });

    it('describes a negative duration as an overrun rather than a minus sign', () => {
      // "2h 15m over" is what an agent needs to read; "-135m" is not.
      expect(formatDuration(-135)).toBe('2h 15m over');
    });

    it('handles zero and missing values', () => {
      expect(formatDuration(0)).toBe('now');
      expect(formatDuration(null)).toBe('—');
      expect(formatDuration(undefined)).toBe('—');
    });
  });

  describe('numbers and currency', () => {
    it('groups in the Indian numbering system', () => {
      // Lakhs and crores: 1,23,45,678 rather than 12,345,678.
      expect(formatNumber(12345678)).toBe('1,23,45,678');
    });

    it('formats rupees', () => {
      const formatted = formatCurrency(125000);
      expect(formatted).toContain('1,25,000');
      expect(formatted).toMatch(/₹|INR/);
    });

    it('renders an em dash for a missing amount', () => {
      expect(formatCurrency(null)).toBe('—');
      expect(formatNumber(undefined)).toBe('—');
    });
  });

  describe('text helpers', () => {
    it('takes initials from the first and last name', () => {
      expect(initials('Priya Raghavan')).toBe('PR');
      expect(initials('Karthik Subramanian Iyer')).toBe('KI');
      expect(initials('Meera')).toBe('ME');
    });

    it('falls back for a missing name rather than rendering "undefined"', () => {
      expect(initials(null)).toBe('?');
      expect(initials('   ')).toBe('?');
    });

    it('splits enum names into readable words', () => {
      expect(humanise('InProgress')).toBe('In progress');
      expect(humanise('AwaitingRequester')).toBe('Awaiting requester');
      expect(humanise('New')).toBe('New');
      expect(humanise(null)).toBe('—');
    });
  });
});

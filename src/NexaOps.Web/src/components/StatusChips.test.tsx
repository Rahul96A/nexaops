import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ThemeProvider } from '@mui/material';
import type { SlaInstance } from '@/api/types';
import { lightTheme } from '@/theme/theme';
import { PriorityChip, SlaBadge, SlaMeter, StatusChip, UserChip } from './StatusChips';

function renderWithTheme(node: React.ReactNode) {
  return render(<ThemeProvider theme={lightTheme}>{node}</ThemeProvider>);
}

function clock(overrides: Partial<SlaInstance> = {}): SlaInstance {
  return {
    id: 'sla-1',
    name: 'P2 high resolution',
    targetType: 'Resolution',
    state: 'InProgress',
    startedAt: '2026-09-07T03:30:00Z',
    dueAt: '2026-09-07T11:30:00Z',
    completedAt: null,
    breachedAt: null,
    durationMinutes: 480,
    elapsedMinutes: 120,
    remainingMinutes: 360,
    consumedPercent: 25,
    warningThresholdPercent: 80,
    ...overrides,
  };
}

describe('service desk vocabulary', () => {
  describe('PriorityChip', () => {
    it('labels each priority the way an agent reads it', () => {
      renderWithTheme(<PriorityChip priority="P1Critical" />);
      expect(screen.getByText('P1 Critical')).toBeInTheDocument();
    });

    it('renders every priority without falling through', () => {
      const priorities = ['P1Critical', 'P2High', 'P3Moderate', 'P4Low', 'P5Planning'] as const;

      for (const priority of priorities) {
        const { unmount } = renderWithTheme(<PriorityChip priority={priority} />);
        expect(screen.getByText(/^P[1-5] /)).toBeInTheDocument();
        unmount();
      }
    });
  });

  describe('StatusChip', () => {
    it('renders a status as readable words rather than a PascalCase enum', () => {
      renderWithTheme(<StatusChip status="InProgress" />);
      expect(screen.getByText('In progress')).toBeInTheDocument();
    });
  });

  describe('SlaMeter', () => {
    it('shows the time left while a commitment is running', () => {
      renderWithTheme(<SlaMeter sla={clock()} />);

      expect(screen.getByText('Resolution')).toBeInTheDocument();
      expect(screen.getByText('6h left')).toBeInTheDocument();
    });

    it('reports an overrun rather than a negative remaining time', () => {
      renderWithTheme(
        <SlaMeter
          sla={clock({ state: 'Breached', remainingMinutes: -135, consumedPercent: 128 })}
        />,
      );

      expect(screen.getByText('2h 15m over')).toBeInTheDocument();
    });

    it('says a paused clock is paused rather than counting down', () => {
      renderWithTheme(<SlaMeter sla={clock({ state: 'Paused' })} />);
      expect(screen.getByText('Paused')).toBeInTheDocument();
    });

    it('says a met commitment is met', () => {
      renderWithTheme(
        <SlaMeter sla={clock({ state: 'Met', completedAt: '2026-09-07T06:00:00Z' })} />,
      );

      expect(screen.getByText('Met')).toBeInTheDocument();
    });

    it('caps the progress bar at 100 percent while still reporting the true overrun', () => {
      renderWithTheme(
        <SlaMeter sla={clock({ state: 'Breached', remainingMinutes: -600, consumedPercent: 220 })} />,
      );

      const bar = screen.getByRole('progressbar');
      expect(bar).toHaveAttribute('aria-valuenow', '100');

      // The bar is clamped, but the label still tells the truth.
      expect(screen.getByText('10h over')).toBeInTheDocument();
    });

    it('is labelled for screen readers', () => {
      renderWithTheme(<SlaMeter sla={clock()} />);
      expect(screen.getByLabelText(/Resolution SLA, 25 percent consumed/)).toBeInTheDocument();
    });
  });

  describe('SlaBadge', () => {
    it('marks a breach unambiguously', () => {
      renderWithTheme(<SlaBadge breached dueAt="2026-09-07T11:30:00Z" />);
      expect(screen.getByText('Breached')).toBeInTheDocument();
    });

    it('renders an em dash when there is no live commitment', () => {
      renderWithTheme(<SlaBadge breached={false} dueAt={null} />);
      expect(screen.getByText('—')).toBeInTheDocument();
    });
  });

  describe('UserChip', () => {
    it('shows the person with their initials', () => {
      renderWithTheme(<UserChip name="Priya Raghavan" color="#1B4DFF" />);

      expect(screen.getByText('Priya Raghavan')).toBeInTheDocument();
      expect(screen.getByText('PR')).toBeInTheDocument();
    });

    it('says "Unassigned" rather than rendering an empty avatar', () => {
      renderWithTheme(<UserChip name={null} />);
      expect(screen.getByText('Unassigned')).toBeInTheDocument();
    });
  });
});

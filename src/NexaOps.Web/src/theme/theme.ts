import { createTheme, type ThemeOptions } from '@mui/material/styles';
import type { Priority, SlaState } from '@/api/types';

/**
 * The NexaOps design system.
 *
 * The palette is deliberately restrained. An agent looks at this screen for eight hours, and
 * colour here carries meaning - priority, SLA state, breach - so it is spent on those signals
 * rather than on decoration. Everything structural is neutral grey.
 */

const brand = {
  50: '#EEF2FF',
  100: '#DCE4FF',
  200: '#B9C8FF',
  300: '#8EA5FF',
  400: '#5B79FF',
  500: '#1B4DFF',
  600: '#0F3AD9',
  700: '#0A2CA8',
  800: '#082283',
  900: '#061A66',
} as const;

const neutral = {
  50: '#F7F8FA',
  100: '#EEF0F4',
  200: '#E3E8F0',
  300: '#CBD3DF',
  400: '#98A4B8',
  500: '#697588',
  600: '#4A5567',
  700: '#333D4D',
  800: '#1F2735',
  900: '#121822',
} as const;

/** Semantic colours for the five priorities. P1 is the only true red on the screen. */
export const priorityColors: Record<Priority, { main: string; contrast: string; label: string }> = {
  P1Critical: { main: '#C62828', contrast: '#FFFFFF', label: 'P1 Critical' },
  P2High: { main: '#E65100', contrast: '#FFFFFF', label: 'P2 High' },
  P3Moderate: { main: '#F9A825', contrast: '#1F2735', label: 'P3 Moderate' },
  P4Low: { main: '#2E7D32', contrast: '#FFFFFF', label: 'P4 Low' },
  P5Planning: { main: '#546E7A', contrast: '#FFFFFF', label: 'P5 Planning' },
};

/** Semantic colours for SLA clock state. */
export const slaColors: Record<SlaState, string> = {
  InProgress: '#1B4DFF',
  Paused: '#697588',
  Met: '#2E7D32',
  Breached: '#C62828',
  Cancelled: '#98A4B8',
};

const shared: ThemeOptions = {
  shape: { borderRadius: 10 },

  typography: {
    fontFamily: [
      'Inter',
      '-apple-system',
      'BlinkMacSystemFont',
      'Segoe UI',
      'Roboto',
      'Helvetica Neue',
      'Arial',
      'sans-serif',
    ].join(','),

    h1: { fontSize: '1.75rem', fontWeight: 700, letterSpacing: '-0.02em' },
    h2: { fontSize: '1.375rem', fontWeight: 700, letterSpacing: '-0.015em' },
    h3: { fontSize: '1.125rem', fontWeight: 600 },
    h4: { fontSize: '1rem', fontWeight: 600 },
    h5: { fontSize: '0.9375rem', fontWeight: 600 },
    h6: { fontSize: '0.875rem', fontWeight: 600 },
    body1: { fontSize: '0.9375rem' },
    body2: { fontSize: '0.875rem' },
    button: { textTransform: 'none', fontWeight: 600 },

    // Used for metadata lines: dense, quiet, and never competing with the record itself.
    caption: { fontSize: '0.75rem', letterSpacing: '0.01em' },
  },

  components: {
    MuiButton: {
      defaultProps: { disableElevation: true },
      styleOverrides: {
        root: { borderRadius: 8, paddingInline: 16 },
      },
    },

    MuiCard: {
      defaultProps: { elevation: 0 },
      styleOverrides: {
        root: ({ theme }) => ({
          border: `1px solid ${theme.palette.divider}`,
          borderRadius: 12,
        }),
      },
    },

    MuiChip: {
      styleOverrides: {
        root: { fontWeight: 600, borderRadius: 6 },
        sizeSmall: { height: 22, fontSize: '0.75rem' },
      },
    },

    MuiTableCell: {
      styleOverrides: {
        root: ({ theme }) => ({
          borderBottom: `1px solid ${theme.palette.divider}`,
          paddingBlock: 10,
        }),
        head: ({ theme }) => ({
          fontWeight: 600,
          fontSize: '0.8125rem',
          color: theme.palette.text.secondary,
          backgroundColor: theme.palette.mode === 'light' ? neutral[50] : neutral[800],
          whiteSpace: 'nowrap',
        }),
      },
    },

    MuiTooltip: {
      defaultProps: { arrow: true },
    },

    MuiTextField: {
      defaultProps: { size: 'small' },
    },

    MuiSelect: {
      defaultProps: { size: 'small' },
    },

    MuiCssBaseline: {
      styleOverrides: {
        // A visible focus ring on every interactive element, not only on those a designer
        // remembered. Keyboard users navigate this application all day.
        '*:focus-visible': {
          outline: `2px solid ${brand[500]}`,
          outlineOffset: 2,
        },
      },
    },
  },
};

export const lightTheme = createTheme({
  ...shared,
  palette: {
    mode: 'light',
    primary: { main: brand[500], light: brand[300], dark: brand[700], contrastText: '#FFFFFF' },
    secondary: { main: '#7A3BFF' },
    error: { main: '#C62828' },
    warning: { main: '#E65100' },
    info: { main: brand[500] },
    success: { main: '#2E7D32' },
    background: { default: neutral[50], paper: '#FFFFFF' },
    text: { primary: neutral[900], secondary: neutral[500] },
    divider: neutral[200],
  },
});

export const darkTheme = createTheme({
  ...shared,
  palette: {
    mode: 'dark',
    primary: { main: brand[400], light: brand[300], dark: brand[600], contrastText: '#FFFFFF' },
    secondary: { main: '#A87BFF' },
    error: { main: '#EF5350' },
    warning: { main: '#FFA726' },
    info: { main: brand[300] },
    success: { main: '#66BB6A' },
    background: { default: '#0D1117', paper: '#161B24' },
    text: { primary: '#E6EAF2', secondary: '#98A4B8' },
    divider: '#28303D',
  },
});

import type { ReactNode } from 'react';
import { Box, Card, CardActionArea, CardContent, Typography } from '@mui/material';
import { formatNumber } from '@/utils/format';

/**
 * A single dashboard figure.
 *
 * Every value shown through this component comes from a live API query. Nothing on the
 * dashboard is a hard-coded statistic.
 *
 * Counts are passed as numbers and formatted here. A string is accepted only for values that are
 * not counts — a rate, or the em dash a rate becomes when there is no denominator to divide by,
 * which must not be rendered as a zero.
 */
export function StatCard({
  label,
  value,
  icon,
  tone = 'default',
  caption,
  onClick,
}: {
  label: string;
  value: number | string;
  icon?: ReactNode;
  tone?: 'default' | 'critical' | 'warning' | 'success';
  caption?: string;
  onClick?: () => void;
}) {
  const toneColor =
    tone === 'critical'
      ? 'error.main'
      : tone === 'warning'
        ? 'warning.main'
        : tone === 'success'
          ? 'success.main'
          : 'text.primary';

  const content = (
    <CardContent sx={{ py: 2.5 }}>
      <Box sx={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between' }}>
        <Box sx={{ minWidth: 0 }}>
          <Typography
            variant="caption"
            sx={{ textTransform: 'uppercase', letterSpacing: '0.06em', fontWeight: 600 }}
            color="text.secondary"
          >
            {label}
          </Typography>

          <Typography variant="h1" sx={{ mt: 0.5, color: toneColor, lineHeight: 1.1 }}>
            {typeof value === 'number' ? formatNumber(value) : value}
          </Typography>

          {caption && (
            <Typography variant="caption" color="text.secondary">
              {caption}
            </Typography>
          )}
        </Box>

        {icon && (
          <Box sx={{ color: toneColor, opacity: 0.35, '& svg': { fontSize: 30 } }}>{icon}</Box>
        )}
      </Box>
    </CardContent>
  );

  if (!onClick) {
    return <Card sx={{ height: '100%' }}>{content}</Card>;
  }

  return (
    <Card sx={{ height: '100%' }}>
      <CardActionArea onClick={onClick} sx={{ height: '100%' }}>
        {content}
      </CardActionArea>
    </Card>
  );
}

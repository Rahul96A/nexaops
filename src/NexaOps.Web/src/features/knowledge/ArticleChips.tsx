import { Chip, Tooltip } from '@mui/material';
import type { ArticleStatus } from '@/api/types';

type ChipColour = 'default' | 'primary' | 'secondary' | 'error' | 'info' | 'success' | 'warning';

const labels: Record<ArticleStatus, string> = {
  Draft: 'Draft',
  InReview: 'In review',
  Published: 'Published',
  Stale: 'Needs re-checking',
  Retired: 'Retired',
};

const help: Record<ArticleStatus, string> = {
  Draft: 'Being written. Not visible to readers.',
  InReview: 'Written and waiting on a reviewer.',
  Published: 'Verified and current.',

  // Warning, not error: the article is still readable and still useful. It just has not been
  // checked recently, and the reader deserves to know that rather than have it hidden.
  Stale: 'Past its review date. Still readable, but nobody has verified it recently.',

  Retired: 'Withdrawn. Kept for the record.',
};

const colours: Record<ArticleStatus, ChipColour> = {
  Draft: 'default',
  InReview: 'info',
  Published: 'success',
  Stale: 'warning',
  Retired: 'default',
};

export function ArticleStatusChip({
  status,
  size = 'small',
}: {
  status: ArticleStatus;
  size?: 'small' | 'medium';
}) {
  return (
    <Tooltip title={help[status] ?? ''}>
      <Chip
        label={labels[status] ?? status}
        color={colours[status] ?? 'default'}
        size={size}
        variant={status === 'Published' || status === 'Stale' ? 'filled' : 'outlined'}
      />
    </Tooltip>
  );
}

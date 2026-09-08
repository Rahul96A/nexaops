import type { ReactNode } from 'react';
import { Box, Typography } from '@mui/material';
import InboxOutlinedIcon from '@mui/icons-material/InboxOutlined';

/**
 * Shown where a list has no rows.
 *
 * An empty queue is usually good news rather than an error, so this reads as a statement of
 * fact with a next action, not as a failure.
 */
export function EmptyState({
  title,
  description,
  icon,
  action,
}: {
  title: string;
  description?: string;
  icon?: ReactNode;
  action?: ReactNode;
}) {
  return (
    <Box
      sx={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        textAlign: 'center',
        py: 8,
        px: 3,
        color: 'text.secondary',
      }}
    >
      <Box sx={{ mb: 2, opacity: 0.5, '& svg': { fontSize: 48 } }}>
        {icon ?? <InboxOutlinedIcon />}
      </Box>

      <Typography variant="h3" component="p" color="text.primary" gutterBottom>
        {title}
      </Typography>

      {description && (
        <Typography variant="body2" sx={{ maxWidth: 420, mb: action ? 3 : 0 }}>
          {description}
        </Typography>
      )}

      {action}
    </Box>
  );
}

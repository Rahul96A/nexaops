import {
  Box,
  CardContent,
  Chip,
  Skeleton,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Tooltip,
  Typography,
} from '@mui/material';
import type { AuditEvent } from '@/api/types';
import { ErrorState } from '@/components/ErrorState';
import { EmptyState } from '@/components/EmptyState';
import { formatLongDateTime, humanise } from '@/utils/format';

/**
 * The record's audit history.
 *
 * These rows are the same append-only events the administration audit screen reads, filtered to
 * this record. Nothing here is editable or deletable, because no such path exists anywhere in
 * the product.
 */
export function IncidentAuditTab({
  events,
  isLoading,
  error,
}: {
  events: AuditEvent[];
  isLoading: boolean;
  error: unknown;
}) {
  if (isLoading) {
    return (
      <CardContent>
        <Stack spacing={1}>
          {Array.from({ length: 6 }, (_, index) => (
            <Skeleton key={index} variant="rounded" height={44} />
          ))}
        </Stack>
      </CardContent>
    );
  }

  if (error) {
    return (
      <CardContent>
        <ErrorState error={error} />
      </CardContent>
    );
  }

  if (events.length === 0) {
    return (
      <EmptyState
        title="No audit events"
        description="Changes to this incident will be recorded here."
      />
    );
  }

  return (
    <Box sx={{ overflowX: 'auto' }}>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>When</TableCell>
            <TableCell>Who</TableCell>
            <TableCell>Action</TableCell>
            <TableCell>Changed</TableCell>
            <TableCell>Source</TableCell>
          </TableRow>
        </TableHead>

        <TableBody>
          {events.map((event) => (
            <TableRow key={event.id} hover>
              <TableCell sx={{ whiteSpace: 'nowrap' }}>
                <Typography variant="caption">{formatLongDateTime(event.occurredAt)}</Typography>
              </TableCell>

              <TableCell>
                <Typography variant="body2">{event.actorDisplayName ?? 'System'}</Typography>
              </TableCell>

              <TableCell>
                <Chip
                  label={humanise(event.action)}
                  size="small"
                  variant="outlined"
                  color={event.outcome === 'Success' ? 'default' : 'error'}
                />
              </TableCell>

              <TableCell sx={{ maxWidth: 320 }}>
                {event.changedFields ? (
                  <Tooltip title={describeChange(event)}>
                    <Typography variant="caption" noWrap>
                      {event.changedFields
                        .split(',')
                        .map((field) => humanise(field.trim()))
                        .join(', ')}
                    </Typography>
                  </Tooltip>
                ) : (
                  <Typography variant="caption" color="text.secondary" noWrap>
                    {event.message ?? '—'}
                  </Typography>
                )}
              </TableCell>

              <TableCell>
                <Typography variant="caption" color="text.secondary">
                  {event.source}
                </Typography>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </Box>
  );
}

function describeChange(event: AuditEvent): string {
  if (!event.beforeJson && !event.afterJson) {
    return event.message ?? '';
  }

  return `Before: ${event.beforeJson ?? 'n/a'} | After: ${event.afterJson ?? 'n/a'}`;
}

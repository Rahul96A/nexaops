import { Alert, AlertTitle, Box, Button } from '@mui/material';
import RefreshIcon from '@mui/icons-material/Refresh';
import { ApiError } from '@/api/client';

/**
 * Renders a failed request honestly.
 *
 * The correlation id is surfaced deliberately: it is the one thing a customer can quote that
 * lets support find the exact request in the logs, and hiding it turns a two-minute diagnosis
 * into a long conversation.
 */
export function ErrorState({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const apiError = error instanceof ApiError ? error : null;

  const title = apiError?.problem.title ?? 'Something went wrong';
  const detail =
    apiError?.userMessage ??
    (error instanceof Error ? error.message : 'The request could not be completed.');

  return (
    <Box sx={{ py: 3 }}>
      <Alert
        severity={apiError?.isForbidden ? 'warning' : 'error'}
        action={
          onRetry && (
            <Button color="inherit" size="small" startIcon={<RefreshIcon />} onClick={onRetry}>
              Retry
            </Button>
          )
        }
      >
        <AlertTitle>{title}</AlertTitle>
        {detail}

        {apiError?.problem.correlationId && (
          <Box
            component="div"
            sx={{ mt: 1, fontFamily: 'monospace', fontSize: '0.75rem', opacity: 0.8 }}
          >
            Reference: {apiError.problem.correlationId}
          </Box>
        )}
      </Alert>
    </Box>
  );
}

import { useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import {
  Alert,
  AlertTitle,
  Avatar,
  Badge,
  Box,
  Button,
  Chip,
  CircularProgress,
  Divider,
  Drawer,
  IconButton,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import AutoAwesomeOutlinedIcon from '@mui/icons-material/AutoAwesomeOutlined';
import CloseIcon from '@mui/icons-material/Close';
import SendIcon from '@mui/icons-material/Send';
import { aiApi } from '@/api/ai';
import { ApiError } from '@/api/client';
import type { AiTurn } from '@/api/types';
import { Permissions } from '@/auth/permissions';
import { useAuth } from '@/auth/useAuth';
import { initials } from '@/utils/format';

/**
 * The AI assistant.
 *
 * Two product commitments are visible in this component. First, the button only appears when
 * the caller holds the assistant permission and the environment actually has a provider - a
 * feature that cannot work is not offered. Second, when AI is unavailable the panel says so
 * plainly; there is no fallback that answers from nothing, because a confident wrong answer
 * about a P1 is worse than no answer.
 */
export function AiAssistantButton() {
  const [open, setOpen] = useState(false);
  const { hasPermission } = useAuth();

  const status = useQuery({
    queryKey: ['ai', 'status'],
    queryFn: ({ signal }) => aiApi.status(signal),
    staleTime: 5 * 60_000,
  });

  if (!hasPermission(Permissions.aiAssistantUse)) {
    return null;
  }

  const configured = status.data?.isConfigured ?? false;

  return (
    <>
      <Tooltip title={configured ? 'Ask the AI assistant' : 'AI assistant is not configured'}>
        <span>
          <IconButton
            onClick={() => setOpen(true)}
            aria-label="Open the AI assistant"
            color={configured ? 'primary' : 'default'}
          >
            <Badge
              variant="dot"
              color={configured ? 'success' : 'default'}
              invisible={!status.isSuccess}
            >
              <AutoAwesomeOutlinedIcon />
            </Badge>
          </IconButton>
        </span>
      </Tooltip>

      <AiAssistantDrawer open={open} onClose={() => setOpen(false)} />
    </>
  );
}

function AiAssistantDrawer({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { profile } = useAuth();
  const [question, setQuestion] = useState('');
  const [history, setHistory] = useState<AiTurn[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [unavailable, setUnavailable] = useState<string | null>(null);

  const status = useQuery({
    queryKey: ['ai', 'status'],
    queryFn: ({ signal }) => aiApi.status(signal),
    enabled: open,
  });

  const ask = useMutation({
    mutationFn: (asked: string) => aiApi.ask(asked, history),
    onSuccess: (answer, asked) => {
      setHistory((previous) => [
        ...previous,
        { role: 'user', content: asked },
        { role: 'assistant', content: answer.answer },
      ]);
      setQuestion('');
      setError(null);
    },
    onError: (mutationError) => {
      if (mutationError instanceof ApiError && mutationError.isAiUnavailable) {
        setUnavailable(mutationError.userMessage);
        return;
      }

      setError(
        mutationError instanceof ApiError
          ? mutationError.userMessage
          : 'The assistant could not answer that.',
      );
    },
  });

  const lastAnswerTools = ask.data?.toolsUsed ?? [];
  const isConfigured = status.data?.isConfigured ?? false;

  const suggestions = [
    'How many P1 incidents are open right now?',
    'Which open incidents have breached their SLA?',
    'Summarise the incidents assigned to me.',
    'What was raised today?',
  ];

  return (
    <Drawer
      anchor="right"
      open={open}
      onClose={onClose}
      slotProps={{ paper: { sx: { width: { xs: '100%', sm: 460 } } } }}
    >
      <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
        <Box
          sx={{
            px: 2,
            py: 1.5,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            borderBottom: 1,
            borderColor: 'divider',
          }}
        >
          <Stack direction="row" spacing={1} alignItems="center">
            <AutoAwesomeOutlinedIcon color="primary" />
            <Box>
              <Typography variant="h4">AI assistant</Typography>
              <Typography variant="caption" color="text.secondary">
                {isConfigured
                  ? `Grounded in your ${profile?.tenantName} data`
                  : 'Not configured in this environment'}
              </Typography>
            </Box>
          </Stack>

          <IconButton onClick={onClose} aria-label="Close the assistant">
            <CloseIcon />
          </IconButton>
        </Box>

        <Box sx={{ flex: 1, overflowY: 'auto', p: 2 }}>
          {status.isLoading && (
            <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
              <CircularProgress size={24} />
            </Box>
          )}

          {status.data && !status.data.isConfigured && (
            <Alert severity="info" sx={{ mb: 2 }}>
              <AlertTitle>AI is not available here</AlertTitle>
              {status.data.unavailableReason}
              <Typography variant="caption" display="block" sx={{ mt: 1 }}>
                NexaOps does not simulate AI answers. When Azure OpenAI is configured for this
                environment, the assistant answers from your live service desk data and shows
                which queries it ran.
              </Typography>
            </Alert>
          )}

          {unavailable && (
            <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setUnavailable(null)}>
              {unavailable}
            </Alert>
          )}

          {error && (
            <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError(null)}>
              {error}
            </Alert>
          )}

          {history.length === 0 && isConfigured && (
            <Box sx={{ py: 2 }}>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                Ask about your service desk. Answers come from the same records you can already
                see, and only from tools your role permits.
              </Typography>

              <Stack spacing={1}>
                {suggestions.map((suggestion) => (
                  <Chip
                    key={suggestion}
                    label={suggestion}
                    variant="outlined"
                    onClick={() => {
                      setQuestion(suggestion);
                      ask.mutate(suggestion);
                    }}
                    sx={{ justifyContent: 'flex-start', height: 'auto', py: 1, '& .MuiChip-label': { whiteSpace: 'normal' } }}
                  />
                ))}
              </Stack>
            </Box>
          )}

          <Stack spacing={2}>
            {history.map((turn, index) => (
              <Stack key={index} direction="row" spacing={1.5} alignItems="flex-start">
                <Avatar
                  sx={{
                    width: 30,
                    height: 30,
                    fontSize: 12,
                    fontWeight: 700,
                    bgcolor: turn.role === 'user' ? (profile?.avatarColor ?? 'primary.main') : 'secondary.main',
                  }}
                >
                  {turn.role === 'user' ? (
                    initials(profile?.displayName)
                  ) : (
                    <AutoAwesomeOutlinedIcon sx={{ fontSize: 16 }} />
                  )}
                </Avatar>

                <Box
                  sx={{
                    flex: 1,
                    p: 1.5,
                    borderRadius: 2,
                    bgcolor: turn.role === 'user' ? 'action.hover' : 'background.paper',
                    border: 1,
                    borderColor: turn.role === 'user' ? 'transparent' : 'divider',
                  }}
                >
                  <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap', lineHeight: 1.65 }}>
                    {turn.content}
                  </Typography>
                </Box>
              </Stack>
            ))}

            {ask.isPending && (
              <Stack direction="row" spacing={1.5} alignItems="center" sx={{ pl: 5.5 }}>
                <CircularProgress size={16} />
                <Typography variant="caption" color="text.secondary">
                  Querying your service desk…
                </Typography>
              </Stack>
            )}
          </Stack>

          {lastAnswerTools.length > 0 && (
            <Box sx={{ mt: 2, pl: 5.5 }}>
              <Typography variant="caption" color="text.secondary" display="block" gutterBottom>
                Answered using:
              </Typography>
              <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>
                {lastAnswerTools.map((tool) => (
                  <Chip key={tool} label={tool} size="small" variant="outlined" />
                ))}
              </Stack>
            </Box>
          )}
        </Box>

        <Divider />

        <Box sx={{ p: 2 }}>
          <Stack direction="row" spacing={1} alignItems="flex-end">
            <TextField
              value={question}
              onChange={(event) => setQuestion(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter' && !event.shiftKey && question.trim()) {
                  event.preventDefault();
                  ask.mutate(question.trim());
                }
              }}
              placeholder={isConfigured ? 'Ask a question…' : 'AI is not configured'}
              disabled={!isConfigured || ask.isPending}
              multiline
              maxRows={4}
              fullWidth
            />

            <Button
              variant="contained"
              onClick={() => ask.mutate(question.trim())}
              disabled={!isConfigured || ask.isPending || question.trim().length === 0}
              sx={{ minWidth: 44, px: 0 }}
              aria-label="Send"
            >
              <SendIcon fontSize="small" />
            </Button>
          </Stack>

          <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 1 }}>
            The assistant can read but not change records. It sees only what your role permits.
          </Typography>
        </Box>
      </Box>
    </Drawer>
  );
}

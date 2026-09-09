import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQuery } from '@tanstack/react-query';
import {
  Alert,
  AlertTitle,
  Avatar,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  CircularProgress,
  Drawer,
  IconButton,
  MenuItem,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import SupportAgentOutlinedIcon from '@mui/icons-material/SupportAgentOutlined';
import CloseIcon from '@mui/icons-material/Close';
import SendIcon from '@mui/icons-material/Send';
import MenuBookOutlinedIcon from '@mui/icons-material/MenuBookOutlined';
import { agentApi, aiApi } from '@/api/ai';
import { ApiError } from '@/api/client';
import type { AiTurn, Urgency, VirtualAgentArticle, VirtualAgentProposal } from '@/api/types';
import { Permissions } from '@/auth/permissions';
import { useAuth } from '@/auth/useAuth';

interface Exchange {
  role: 'user' | 'assistant';
  content: string;
  articles?: VirtualAgentArticle[];
  proposal?: VirtualAgentProposal | null;
}

const urgencies: { value: Urgency; label: string }[] = [
  { value: 'Critical', label: 'It has stopped my team working' },
  { value: 'High', label: 'I cannot do my job' },
  { value: 'Medium', label: 'It is getting in the way' },
  { value: 'Low', label: 'It can wait' },
];

/**
 * The employee-facing virtual agent.
 *
 * Deliberately a different thing from the staff assistant sitting next to it. This one is for
 * somebody who has a problem rather than somebody working a queue, so it leads with published
 * guidance and ends — when nothing else helped — with an offer to raise a ticket.
 *
 * The offer is the point. An agent that can only search is a worse search box; an agent that
 * files tickets on its own is a liability. What the person sees is a filled-in form they can
 * correct before pressing the button, and the ticket is theirs, raised by them.
 */
export function VirtualAgentButton() {
  const navigate = useNavigate();
  const { hasPermission } = useAuth();

  const [open, setOpen] = useState(false);
  const [message, setMessage] = useState('');
  const [exchanges, setExchanges] = useState<Exchange[]>([]);
  const [raised, setRaised] = useState<{ number: string; id: string } | null>(null);

  const bottom = useRef<HTMLDivElement>(null);

  const status = useQuery({
    queryKey: ['ai-status'],
    queryFn: ({ signal }) => aiApi.status(signal),
    enabled: hasPermission(Permissions.aiAgentUse),
  });

  const send = useMutation({
    mutationFn: (text: string) => {
      const history: AiTurn[] = exchanges.map((e) => ({ role: e.role, content: e.content }));
      return agentApi.chat(text, history);
    },
    onSuccess: (reply) => {
      setExchanges((current) => [
        ...current,
        {
          role: 'assistant',
          content: reply.reply,
          articles: reply.articles,
          proposal: reply.proposal,
        },
      ]);
    },
  });

  useEffect(() => {
    bottom.current?.scrollIntoView({ behavior: 'smooth' });
  }, [exchanges, send.isPending]);

  // Hidden entirely when the caller cannot use it. A button that always fails teaches people to
  // ignore the product's buttons.
  if (!hasPermission(Permissions.aiAgentUse)) {
    return null;
  }

  function submit() {
    const text = message.trim();

    if (!text || send.isPending) {
      return;
    }

    setExchanges((current) => [...current, { role: 'user', content: text }]);
    setMessage('');
    setRaised(null);
    send.mutate(text);
  }

  const unavailable = status.data && !status.data.isConfigured;

  return (
    <>
      <Tooltip title="Get help">
        <IconButton onClick={() => setOpen(true)} aria-label="Open the help agent">
          <SupportAgentOutlinedIcon />
        </IconButton>
      </Tooltip>

      <Drawer
        anchor="right"
        open={open}
        onClose={() => setOpen(false)}
        slotProps={{ paper: { sx: { width: { xs: '100%', sm: 460 } } } }}
      >
        <Stack sx={{ height: '100%' }}>
          <Stack
            direction="row"
            alignItems="center"
            gap={1}
            sx={{ p: 2, borderBottom: 1, borderColor: 'divider' }}
          >
            <SupportAgentOutlinedIcon color="primary" />
            <Typography variant="h4" sx={{ flex: 1 }}>
              Get help
            </Typography>
            <IconButton onClick={() => setOpen(false)} aria-label="Close">
              <CloseIcon />
            </IconButton>
          </Stack>

          <Box sx={{ flex: 1, overflowY: 'auto', p: 2 }}>
            {unavailable && (
              <Alert severity="info">
                <AlertTitle>The help agent is not available here</AlertTitle>
                No AI provider is configured in this environment, so there is nothing to answer
                you. You can still raise a ticket from the incidents page.
              </Alert>
            )}

            {!unavailable && exchanges.length === 0 && (
              <Stack gap={2}>
                <Typography variant="body2" color="text.secondary">
                  Describe what is wrong in your own words. I will look for guidance first, and
                  offer to raise a ticket if I cannot find any.
                </Typography>

                <Stack gap={1}>
                  {[
                    'My VPN will not connect',
                    'Where has my laptop request got to?',
                    'I need access to the finance drive',
                  ].map((suggestion) => (
                    <Button
                      key={suggestion}
                      size="small"
                      variant="outlined"
                      sx={{ justifyContent: 'flex-start', textTransform: 'none' }}
                      onClick={() => {
                        setExchanges([{ role: 'user', content: suggestion }]);
                        send.mutate(suggestion);
                      }}
                    >
                      {suggestion}
                    </Button>
                  ))}
                </Stack>
              </Stack>
            )}

            <Stack gap={2}>
              {exchanges.map((exchange, index) => (
                <Stack
                  key={index}
                  direction="row"
                  gap={1.5}
                  sx={{ flexDirection: exchange.role === 'user' ? 'row-reverse' : 'row' }}
                >
                  <Avatar
                    sx={{
                      width: 28,
                      height: 28,
                      bgcolor: exchange.role === 'user' ? 'primary.main' : 'success.main',
                    }}
                  >
                    {exchange.role === 'user' ? (
                      'Y'
                    ) : (
                      <SupportAgentOutlinedIcon sx={{ fontSize: 16 }} />
                    )}
                  </Avatar>

                  <Box sx={{ maxWidth: '85%' }}>
                    <Box
                      sx={{
                        px: 1.5,
                        py: 1,
                        borderRadius: 2,
                        bgcolor: exchange.role === 'user' ? 'action.selected' : 'background.paper',
                        border: 1,
                        borderColor: 'divider',
                      }}
                    >
                      <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
                        {exchange.content}
                      </Typography>
                    </Box>

                    {/*
                      Citations come from the tool result, not from what the model wrote, so a
                      number it invented cannot become a link here.
                    */}
                    {exchange.articles && exchange.articles.length > 0 && (
                      <Stack direction="row" gap={0.5} flexWrap="wrap" sx={{ mt: 0.75 }}>
                        {exchange.articles.map((article) => (
                          <Chip
                            key={article.number}
                            icon={<MenuBookOutlinedIcon />}
                            label={article.number}
                            size="small"
                            variant="outlined"
                            onClick={() => {
                              setOpen(false);
                              navigate(`/knowledge/${article.id}`);
                            }}
                          />
                        ))}
                      </Stack>
                    )}

                    {exchange.proposal && (
                      <ProposalCard
                        proposal={exchange.proposal}
                        onRaised={(result) => {
                          setRaised(result);

                          // The offer is consumed once accepted. Leaving a live button behind
                          // invites a second identical ticket.
                          setExchanges((current) =>
                            current.map((e, i) => (i === index ? { ...e, proposal: null } : e)),
                          );
                        }}
                      />
                    )}
                  </Box>
                </Stack>
              ))}

              {send.isPending && (
                <Stack direction="row" gap={1} alignItems="center">
                  <CircularProgress size={16} />
                  <Typography variant="caption" color="text.secondary">
                    Looking…
                  </Typography>
                </Stack>
              )}

              {send.error instanceof ApiError && (
                <Alert severity={send.error.isAiUnavailable ? 'info' : 'error'}>
                  {send.error.isAiUnavailable
                    ? 'The help agent is not available in this environment.'
                    : send.error.userMessage}
                </Alert>
              )}

              {raised && (
                <Alert
                  severity="success"
                  action={
                    <Button
                      size="small"
                      onClick={() => {
                        setOpen(false);
                        navigate(`/incidents/${raised.id}`);
                      }}
                    >
                      Open
                    </Button>
                  }
                >
                  {raised.number} has been raised. The service desk will pick it up.
                </Alert>
              )}
            </Stack>

            <div ref={bottom} />
          </Box>

          <Stack
            direction="row"
            gap={1}
            sx={{ p: 2, borderTop: 1, borderColor: 'divider' }}
            component="form"
            onSubmit={(event) => {
              event.preventDefault();
              submit();
            }}
          >
            <TextField
              fullWidth
              size="small"
              placeholder="What has gone wrong?"
              value={message}
              onChange={(event) => setMessage(event.target.value)}
              disabled={unavailable || send.isPending}
              multiline
              maxRows={4}
            />
            <IconButton
              type="submit"
              color="primary"
              disabled={!message.trim() || send.isPending || unavailable}
              aria-label="Send"
            >
              <SendIcon />
            </IconButton>
          </Stack>
        </Stack>
      </Drawer>
    </>
  );
}

/**
 * The confirmation card.
 *
 * Editable on purpose. The agent drafted this from a conversation and will sometimes have the
 * emphasis wrong; the person raising the ticket is the one who knows. What they press Send on is
 * what gets filed, and it is filed as theirs.
 */
function ProposalCard({
  proposal,
  onRaised,
}: {
  proposal: VirtualAgentProposal;
  onRaised: (result: { number: string; id: string }) => void;
}) {
  const { hasPermission } = useAuth();

  const [title, setTitle] = useState(proposal.title);
  const [description, setDescription] = useState(proposal.description);
  const [urgency, setUrgency] = useState<Urgency>(proposal.urgency);

  const raise = useMutation({
    mutationFn: () => agentApi.confirm({ kind: proposal.kind, title, description, urgency }),
    onSuccess: (result) => onRaised({ number: result.recordNumber, id: result.recordId }),
  });

  if (!hasPermission(Permissions.aiActionConfirm)) {
    return (
      <Alert severity="info" sx={{ mt: 1 }}>
        Raise this from the incidents page — your account cannot file a ticket from here.
      </Alert>
    );
  }

  return (
    <Card variant="outlined" sx={{ mt: 1 }}>
      <CardContent>
        <Typography variant="subtitle2" gutterBottom>
          Raise this as a ticket?
        </Typography>

        <Stack gap={1.5}>
          {raise.error instanceof ApiError && (
            <Alert severity="error">{raise.error.userMessage}</Alert>
          )}

          <TextField
            size="small"
            label="Summary"
            value={title}
            onChange={(event) => setTitle(event.target.value)}
          />

          <TextField
            size="small"
            label="What is happening"
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            multiline
            minRows={2}
          />

          <TextField
            size="small"
            select
            label="How much is it affecting you?"
            value={urgency}
            onChange={(event) => setUrgency(event.target.value as Urgency)}
          >
            {urgencies.map((option) => (
              <MenuItem key={option.value} value={option.value}>
                {option.label}
              </MenuItem>
            ))}
          </TextField>

          <Button
            variant="contained"
            size="small"
            disabled={!title.trim() || raise.isPending}
            onClick={() => raise.mutate()}
          >
            Raise it
          </Button>

          <Typography variant="caption" color="text.secondary">
            It will be raised in your name, and you can change anything above first.
          </Typography>
        </Stack>
      </CardContent>
    </Card>
  );
}

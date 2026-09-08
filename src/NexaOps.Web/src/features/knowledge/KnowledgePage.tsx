import { useCallback } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import {
  Box,
  Card,
  CardActionArea,
  CardContent,
  Chip,
  InputAdornment,
  LinearProgress,
  Stack,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Tooltip,
  Typography,
} from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import VisibilityOutlinedIcon from '@mui/icons-material/VisibilityOutlined';
import ThumbUpOutlinedIcon from '@mui/icons-material/ThumbUpOutlined';
import { knowledgeApi } from '@/api/knowledge';
import { PageHeader } from '@/components/PageHeader';
import { EmptyState } from '@/components/EmptyState';
import { ErrorState } from '@/components/ErrorState';
import { ArticleStatusChip } from './ArticleChips';

const scopes = [
  { value: '', label: 'All' },
  { value: 'published', label: 'Published' },
  { value: 'stale', label: 'Needs re-checking' },
  { value: 'needs-review', label: 'Awaiting review' },
  { value: 'my-drafts', label: 'My drafts' },
];

/**
 * Knowledge search.
 *
 * A card list rather than a table: an article is chosen by reading its summary, and a row of
 * columns makes that the hardest thing on the page to do.
 */
export function KnowledgePage() {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();

  const scope = params.get('scope') ?? '';
  const search = params.get('search') ?? '';

  const update = useCallback(
    (changes: Record<string, string | null>) => {
      const next = new URLSearchParams(params);

      for (const [key, value] of Object.entries(changes)) {
        if (value === null || value === '') {
          next.delete(key);
        } else {
          next.set(key, value);
        }
      }

      setParams(next, { replace: true });
    },
    [params, setParams],
  );

  const { data, isLoading, error, refetch } = useQuery({
    queryKey: ['knowledge', scope, search],
    queryFn: ({ signal }) =>
      knowledgeApi.search({ scope: scope || undefined, search: search || undefined }, signal),
    placeholderData: keepPreviousData,
  });

  return (
    <Box>
      <PageHeader
        title="Knowledge"
        subtitle="How to do things, and what to do when they break."
      />

      <Stack direction={{ xs: 'column', md: 'row' }} gap={2} sx={{ mb: 3 }} alignItems="center">
        <ToggleButtonGroup
          exclusive
          size="small"
          value={scope}
          onChange={(_, value: string | null) => update({ scope: value ?? '' })}
        >
          {scopes.map((option) => (
            <ToggleButton key={option.value || 'all'} value={option.value}>
              {option.label}
            </ToggleButton>
          ))}
        </ToggleButtonGroup>

        <TextField
          size="small"
          placeholder="Search titles, content and keywords"
          defaultValue={search}
          onBlur={(event) => update({ search: event.target.value })}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              update({ search: (event.target as HTMLInputElement).value });
            }
          }}
          sx={{ minWidth: 360 }}
          slotProps={{
            input: {
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon fontSize="small" />
                </InputAdornment>
              ),
            },
          }}
        />
      </Stack>

      {isLoading && <LinearProgress />}

      {error && <ErrorState error={error} onRetry={() => void refetch()} />}

      {data && data.items.length === 0 && (
        <EmptyState
          title="Nothing here"
          description={
            search
              ? 'No article matches that search. Try a different word, or raise a ticket.'
              : 'No articles have been written yet.'
          }
        />
      )}

      <Stack gap={1.5}>
        {(data?.items ?? []).map((article) => (
          <Card key={article.id} variant="outlined">
            <CardActionArea onClick={() => navigate(`/knowledge/${article.id}`)}>
              <CardContent>
                <Stack
                  direction="row"
                  justifyContent="space-between"
                  alignItems="flex-start"
                  gap={2}
                >
                  <Box>
                    <Stack direction="row" gap={1} alignItems="center" flexWrap="wrap">
                      <Typography variant="subtitle2" fontFamily="monospace" color="text.secondary">
                        {article.number}
                      </Typography>
                      <Typography variant="subtitle1" fontWeight={600}>
                        {article.title}
                      </Typography>
                      <ArticleStatusChip status={article.status} />
                      {article.audience === 'ServiceDesk' && (
                        <Chip label="Internal" size="small" variant="outlined" color="warning" />
                      )}
                    </Stack>

                    <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
                      {article.summary}
                    </Typography>
                  </Box>

                  <Stack direction="row" gap={1.5} alignItems="center" sx={{ flexShrink: 0 }}>
                    <Tooltip title="Times read">
                      <Stack direction="row" gap={0.5} alignItems="center">
                        <VisibilityOutlinedIcon fontSize="small" color="disabled" />
                        <Typography variant="caption">{article.viewCount}</Typography>
                      </Stack>
                    </Tooltip>

                    {article.helpfulRatio != null && (
                      <Tooltip title="Share of readers who said it helped">
                        <Stack direction="row" gap={0.5} alignItems="center">
                          <ThumbUpOutlinedIcon fontSize="small" color="disabled" />
                          <Typography variant="caption">
                            {Math.round(article.helpfulRatio * 100)}%
                          </Typography>
                        </Stack>
                      </Tooltip>
                    )}
                  </Stack>
                </Stack>
              </CardContent>
            </CardActionArea>
          </Card>
        ))}
      </Stack>
    </Box>
  );
}

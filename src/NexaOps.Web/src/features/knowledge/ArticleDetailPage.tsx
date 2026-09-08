import { useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Divider,
  LinearProgress,
  Stack,
  Typography,
} from '@mui/material';
import ThumbUpOutlinedIcon from '@mui/icons-material/ThumbUpOutlined';
import ThumbDownOutlinedIcon from '@mui/icons-material/ThumbDownOutlined';
import { knowledgeApi } from '@/api/knowledge';
import { ApiError } from '@/api/client';
import type { ArticleStatus } from '@/api/types';
import { Can } from '@/auth/Can';
import { Permissions } from '@/auth/permissions';
import { PageHeader } from '@/components/PageHeader';
import { ErrorState } from '@/components/ErrorState';
import { ArticleStatusChip } from './ArticleChips';
import { formatDate, formatDateTime } from '@/utils/format';

const transitionLabels: Partial<Record<ArticleStatus, string>> = {
  InReview: 'Submit for review',
  Published: 'Publish',
  Draft: 'Return to draft',
  Retired: 'Retire',
};

export function ArticleDetailPage() {
  const { id = '' } = useParams();
  const queryClient = useQueryClient();

  const { data: article, isLoading, error, refetch } = useQuery({
    queryKey: ['article', id],
    queryFn: ({ signal }) => knowledgeApi.get(id, signal),
    enabled: Boolean(id),
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['article', id] });
    void queryClient.invalidateQueries({ queryKey: ['knowledge'] });
  };

  const changeStatus = useMutation({
    mutationFn: (status: ArticleStatus) => knowledgeApi.changeStatus(id, { status }),
    onSuccess: invalidate,
  });

  const feedback = useMutation({
    mutationFn: (wasHelpful: boolean) => knowledgeApi.feedback(id, { wasHelpful }),
    onSuccess: invalidate,
  });

  if (isLoading) {
    return <LinearProgress />;
  }

  if (error) {
    return <ErrorState error={error} onRetry={() => void refetch()} />;
  }

  if (!article) {
    return null;
  }

  const mutationError = [changeStatus.error, feedback.error].find(
    (e): e is ApiError => e instanceof ApiError,
  );

  return (
    <Box>
      <PageHeader
        title={article.title}
        subtitle={article.summary}
        crumbs={[{ label: 'Knowledge', to: '/knowledge' }, { label: article.number }]}
        actions={
          <Stack direction="row" gap={1} flexWrap="wrap">
            {article.allowedTransitions
              .filter((status) => transitionLabels[status])
              .map((status) => (
                <Can
                  key={status}
                  permission={
                    status === 'Published'
                      ? Permissions.knowledgePublish
                      : status === 'Retired'
                        ? Permissions.knowledgeRetire
                        : Permissions.knowledgeUpdate
                  }
                >
                  <Button
                    variant={status === 'Published' ? 'contained' : 'outlined'}
                    disabled={changeStatus.isPending}
                    onClick={() => changeStatus.mutate(status)}
                  >
                    {transitionLabels[status]}
                  </Button>
                </Can>
              ))}
          </Stack>
        }
      />

      {mutationError && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          {mutationError.userMessage}
        </Alert>
      )}

      {article.status === 'Stale' && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          <strong>Nobody has verified this recently.</strong> It was due for review on{' '}
          {article.reviewDueAt ? formatDate(article.reviewDueAt) : 'an earlier date'}. It is still
          here because old guidance beats none — but check it before relying on it.
        </Alert>
      )}

      {article.status === 'Retired' && (
        <Alert severity="info" sx={{ mb: 2 }}>
          <strong>Withdrawn.</strong> {article.retirementReason}
        </Alert>
      )}

      <Stack direction={{ xs: 'column', lg: 'row' }} gap={3} alignItems="flex-start">
        <Stack gap={3} sx={{ flex: 1, width: '100%' }}>
          <Card variant="outlined">
            <CardContent>
              {/*
                Rendered as pre-wrapped text rather than parsed as Markdown. A Markdown renderer
                is a real dependency with a real XSS surface, and article bodies are written by
                users. Plain text is the honest interim: see docs/STATUS.md.
              */}
              <Typography
                variant="body1"
                sx={{ whiteSpace: 'pre-wrap', fontFamily: 'inherit', lineHeight: 1.7 }}
              >
                {article.body}
              </Typography>
            </CardContent>
          </Card>

          <Card variant="outlined">
            <CardContent>
              <Typography variant="subtitle2" gutterBottom>
                Did this help?
              </Typography>

              <Can
                permission={Permissions.knowledgeFeedback}
                fallback={
                  <Typography variant="body2" color="text.secondary">
                    Feedback is not available to you.
                  </Typography>
                }
              >
                <Stack direction="row" gap={1} alignItems="center">
                  <Button
                    variant={article.myFeedback === true ? 'contained' : 'outlined'}
                    size="small"
                    startIcon={<ThumbUpOutlinedIcon />}
                    disabled={feedback.isPending}
                    onClick={() => feedback.mutate(true)}
                  >
                    Yes
                  </Button>

                  <Button
                    variant={article.myFeedback === false ? 'contained' : 'outlined'}
                    color="inherit"
                    size="small"
                    startIcon={<ThumbDownOutlinedIcon />}
                    disabled={feedback.isPending}
                    onClick={() => feedback.mutate(false)}
                  >
                    No
                  </Button>

                  <Typography variant="caption" color="text.secondary" sx={{ ml: 1 }}>
                    {article.helpfulCount + article.notHelpfulCount === 0
                      ? 'Nobody has said yet.'
                      : `${article.helpfulCount} of ${article.helpfulCount + article.notHelpfulCount} found this helpful.`}
                  </Typography>
                </Stack>
              </Can>
            </CardContent>
          </Card>
        </Stack>

        <Card variant="outlined" sx={{ width: { xs: '100%', lg: 300 }, flexShrink: 0 }}>
          <CardContent>
            <Stack gap={1.5}>
              <Field label="Status">
                <ArticleStatusChip status={article.status} />
              </Field>

              <Field label="Audience">
                <Chip
                  label={article.audience === 'Everyone' ? 'Everyone' : 'Service desk'}
                  size="small"
                  variant="outlined"
                  color={article.audience === 'ServiceDesk' ? 'warning' : 'default'}
                />
              </Field>

              <Field label="Author">{article.authorName}</Field>
              {article.reviewerName && <Field label="Reviewer">{article.reviewerName}</Field>}
              <Field label="Category">{article.categoryName ?? '—'}</Field>
              {article.problemNumber && <Field label="Documents">{article.problemNumber}</Field>}

              <Divider />

              <Field label="Times read">{String(article.viewCount)}</Field>

              {article.publishedAt && (
                <Field label="Published">{formatDateTime(article.publishedAt)}</Field>
              )}
              {article.reviewDueAt && (
                <Field label="Review due">{formatDate(article.reviewDueAt)}</Field>
              )}
            </Stack>
          </CardContent>
        </Card>
      </Stack>
    </Box>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1}>
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
      <Box sx={{ textAlign: 'right' }}>
        {typeof children === 'string' ? (
          <Typography variant="body2">{children}</Typography>
        ) : (
          children
        )}
      </Box>
    </Stack>
  );
}

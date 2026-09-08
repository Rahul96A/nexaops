import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useForm, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useMutation, useQuery } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import SaveIcon from '@mui/icons-material/Save';
import { incidentsApi } from '@/api/incidents';
import { referenceApi } from '@/api/reference';
import { ApiError } from '@/api/client';
import type { Impact, IncidentChannel, Urgency } from '@/api/types';
import { PageHeader } from '@/components/PageHeader';
import { PriorityChip } from '@/components/StatusChips';
import { Can } from '@/auth/Can';
import { Permissions } from '@/auth/permissions';
import { humanise } from '@/utils/format';

const IMPACTS: Impact[] = ['Extensive', 'Significant', 'Moderate', 'Minor'];
const URGENCIES: Urgency[] = ['Critical', 'High', 'Medium', 'Low'];
const CHANNELS: IncidentChannel[] = ['Portal', 'Email', 'Phone', 'Chat', 'WalkIn', 'Monitoring'];

const schema = z.object({
  title: z.string().trim().min(5, 'Give a short summary of at least 5 characters.').max(300),
  description: z
    .string()
    .trim()
    .min(20, 'Describe what is happening in at least 20 characters.')
    .max(20000),
  categoryId: z.string().optional(),
  subcategoryId: z.string().optional(),
  impact: z.enum(['Extensive', 'Significant', 'Moderate', 'Minor']),
  urgency: z.enum(['Critical', 'High', 'Medium', 'Low']),
  channel: z.enum(['Portal', 'Email', 'Phone', 'Chat', 'WalkIn', 'Monitoring']),
  assignmentGroupId: z.string().optional(),
  tags: z.string().optional(),
});

type Values = z.infer<typeof schema>;

/**
 * Raise an incident.
 *
 * The form shows the priority the impact and urgency will produce, live, using the tenant's own
 * matrix from the API. That removes the most common surprise on this screen - an agent choosing
 * severity without realising what it commits the desk to.
 */
export function NewIncidentPage() {
  const navigate = useNavigate();
  const [submitError, setSubmitError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    control,
    watch,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      title: '',
      description: '',
      impact: 'Moderate',
      urgency: 'Medium',
      channel: 'Portal',
      categoryId: '',
      subcategoryId: '',
      assignmentGroupId: '',
      tags: '',
    },
  });

  const impact = watch('impact');
  const urgency = watch('urgency');
  const categoryId = watch('categoryId');

  const categories = useQuery({
    queryKey: ['reference', 'categories'],
    queryFn: ({ signal }) => referenceApi.categories('Incident', signal),
    staleTime: 5 * 60_000,
  });

  const groups = useQuery({
    queryKey: ['reference', 'groups'],
    queryFn: ({ signal }) => referenceApi.groups('Assignment', signal),
    staleTime: 5 * 60_000,
  });

  const matrix = useQuery({
    queryKey: ['reference', 'priority-matrix'],
    queryFn: ({ signal }) => referenceApi.priorityMatrix(signal),
    staleTime: 10 * 60_000,
  });

  const derivedPriority = matrix.data?.find(
    (row) => row.impact === impact && row.urgency === urgency,
  )?.priority;

  const subcategories = useMemo(
    () => categories.data?.find((category) => category.id === categoryId)?.subcategories ?? [],
    [categories.data, categoryId],
  );

  const create = useMutation({
    mutationFn: (values: Values) =>
      incidentsApi.create({
        title: values.title.trim(),
        description: values.description.trim(),
        categoryId: values.categoryId || null,
        subcategoryId: values.subcategoryId || null,
        impact: values.impact,
        urgency: values.urgency,
        channel: values.channel,
        assignmentGroupId: values.assignmentGroupId || null,
        tags: values.tags
          ? values.tags
              .split(',')
              .map((tag) => tag.trim())
              .filter(Boolean)
          : undefined,
      }),
    onSuccess: (incident) => navigate(`/incidents/${incident.id}`),
    onError: (error) =>
      setSubmitError(
        error instanceof ApiError ? error.userMessage : 'The incident could not be raised.',
      ),
  });

  return (
    <>
      <PageHeader
        title="Raise an incident"
        subtitle="Something is broken or degraded. Requests for new things go through the service catalog."
        crumbs={[{ label: 'Incidents', to: '/incidents' }, { label: 'New' }]}
      />

      <Box
        component="form"
        onSubmit={handleSubmit((values) => {
          setSubmitError(null);
          create.mutate(values);
        })}
        noValidate
        sx={{
          display: 'grid',
          gap: 2,
          gridTemplateColumns: { xs: '1fr', lg: 'minmax(0, 1fr) 340px' },
          alignItems: 'start',
        }}
      >
        <Card>
          <CardContent sx={{ p: 3 }}>
            {submitError && (
              <Alert severity="error" sx={{ mb: 2 }}>
                {submitError}
              </Alert>
            )}

            <Stack spacing={3}>
              <TextField
                {...register('title')}
                label="Short summary"
                placeholder="Cannot connect to the corporate VPN"
                required
                autoFocus
                fullWidth
                error={Boolean(errors.title)}
                helperText={errors.title?.message ?? 'One line an agent can scan in a queue.'}
              />

              <TextField
                {...register('description')}
                label="What is happening?"
                placeholder="What were you doing, what happened, what have you already tried, and who else is affected?"
                required
                multiline
                minRows={7}
                fullWidth
                error={Boolean(errors.description)}
                helperText={
                  errors.description?.message ??
                  'The more specific this is, the less back and forth is needed.'
                }
              />

              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                <Controller
                  name="categoryId"
                  control={control}
                  render={({ field }) => (
                    <TextField
                      {...field}
                      select
                      label="Category"
                      fullWidth
                      onChange={(event) => {
                        field.onChange(event);
                        // The old subcategory belongs to the old category.
                        setValue('subcategoryId', '');
                      }}
                    >
                      <MenuItem value="">Not sure</MenuItem>
                      {(categories.data ?? []).map((category) => (
                        <MenuItem key={category.id} value={category.id}>
                          {category.name}
                        </MenuItem>
                      ))}
                    </TextField>
                  )}
                />

                <Controller
                  name="subcategoryId"
                  control={control}
                  render={({ field }) => (
                    <TextField {...field} select label="Subcategory" fullWidth disabled={!categoryId}>
                      <MenuItem value="">Not sure</MenuItem>
                      {subcategories.map((subcategory) => (
                        <MenuItem key={subcategory.id} value={subcategory.id}>
                          {subcategory.name}
                        </MenuItem>
                      ))}
                    </TextField>
                  )}
                />
              </Stack>

              <TextField
                {...register('tags')}
                label="Tags (optional)"
                placeholder="vpn, remote-access"
                fullWidth
                helperText="Comma separated. Useful for grouping related incidents later."
              />
            </Stack>
          </CardContent>
        </Card>

        <Stack spacing={2}>
          <Card>
            <CardContent>
              <Typography variant="h4" gutterBottom>
                Severity
              </Typography>

              <Stack spacing={2}>
                <Controller
                  name="impact"
                  control={control}
                  render={({ field }) => (
                    <TextField
                      {...field}
                      select
                      label="Impact"
                      fullWidth
                      helperText="How much of the business is affected?"
                    >
                      {IMPACTS.map((option) => (
                        <MenuItem key={option} value={option}>
                          {humanise(option)}
                        </MenuItem>
                      ))}
                    </TextField>
                  )}
                />

                <Controller
                  name="urgency"
                  control={control}
                  render={({ field }) => (
                    <TextField
                      {...field}
                      select
                      label="Urgency"
                      fullWidth
                      helperText="How soon does it need to be fixed?"
                    >
                      {URGENCIES.map((option) => (
                        <MenuItem key={option} value={option}>
                          {humanise(option)}
                        </MenuItem>
                      ))}
                    </TextField>
                  )}
                />

                {derivedPriority && (
                  <Box
                    sx={{
                      p: 1.5,
                      borderRadius: 2,
                      bgcolor: 'action.hover',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      gap: 1,
                    }}
                  >
                    <Typography variant="body2" color="text.secondary">
                      Resulting priority
                    </Typography>
                    <PriorityChip priority={derivedPriority} size="medium" />
                  </Box>
                )}

                <Typography variant="caption" color="text.secondary">
                  Priority is derived from your organization's impact and urgency matrix and
                  determines the SLA commitment.
                </Typography>
              </Stack>
            </CardContent>
          </Card>

          <Card>
            <CardContent>
              <Typography variant="h4" gutterBottom>
                Routing
              </Typography>

              <Stack spacing={2}>
                <Controller
                  name="channel"
                  control={control}
                  render={({ field }) => (
                    <TextField {...field} select label="Reported via" fullWidth>
                      {CHANNELS.map((option) => (
                        <MenuItem key={option} value={option}>
                          {humanise(option)}
                        </MenuItem>
                      ))}
                    </TextField>
                  )}
                />

                <Can permission={Permissions.incidentAssign}>
                  <Controller
                    name="assignmentGroupId"
                    control={control}
                    render={({ field }) => (
                      <TextField
                        {...field}
                        select
                        label="Assignment group"
                        fullWidth
                        helperText="Leave blank to route by category."
                      >
                        <MenuItem value="">Route automatically</MenuItem>
                        {(groups.data ?? []).map((group) => (
                          <MenuItem key={group.id} value={group.id}>
                            {group.name}
                          </MenuItem>
                        ))}
                      </TextField>
                    )}
                  />
                </Can>
              </Stack>
            </CardContent>
          </Card>

          <Stack direction="row" spacing={1}>
            <Button
              type="submit"
              variant="contained"
              size="large"
              startIcon={<SaveIcon />}
              disabled={isSubmitting || create.isPending}
              fullWidth
            >
              {create.isPending ? 'Raising…' : 'Raise incident'}
            </Button>

            <Button size="large" onClick={() => navigate('/incidents')}>
              Cancel
            </Button>
          </Stack>

          <Chip
            label="An SLA commitment starts the moment this is raised"
            size="small"
            variant="outlined"
            sx={{ alignSelf: 'flex-start' }}
          />
        </Stack>
      </Box>
    </>
  );
}

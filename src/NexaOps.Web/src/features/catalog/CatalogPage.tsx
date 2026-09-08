import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import {
  Box,
  Card,
  CardActionArea,
  CardContent,
  Chip,
  Grid,
  InputAdornment,
  LinearProgress,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import ScheduleOutlinedIcon from '@mui/icons-material/ScheduleOutlined';
import HowToRegOutlinedIcon from '@mui/icons-material/HowToRegOutlined';
import { catalogApi } from '@/api/requests';
import { PageHeader } from '@/components/PageHeader';
import { EmptyState } from '@/components/EmptyState';
import { ErrorState } from '@/components/ErrorState';
import { formatCurrency } from '@/utils/format';

/**
 * The service catalogue as a requester sees it.
 *
 * Draft and retired items are filtered out by the server, not here - a requester must not be
 * able to order something the business has not finished defining.
 */
export function CatalogPage() {
  const navigate = useNavigate();
  const [search, setSearch] = useState('');

  const { data, isLoading, error, refetch } = useQuery({
    queryKey: ['catalog', search],
    queryFn: ({ signal }) => catalogApi.browse({ search: search || undefined }, signal),
  });

  return (
    <Box>
      <PageHeader
        title="Service catalogue"
        subtitle="Order equipment, software and access. Some items need approval before they are fulfilled."
      />

      <TextField
        value={search}
        onChange={(event) => setSearch(event.target.value)}
        placeholder="Search the catalogue"
        size="small"
        sx={{ mb: 3, maxWidth: 420 }}
        fullWidth
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

      {isLoading && <LinearProgress />}

      {error && <ErrorState error={error} onRetry={() => void refetch()} />}

      {data && data.length === 0 && (
        <EmptyState
          title="Nothing in the catalogue yet"
          description={
            search
              ? 'No catalogue item matches that search.'
              : 'Once your administrator publishes catalogue items, they will appear here.'
          }
        />
      )}

      {data && data.length > 0 && (
        <Grid container spacing={2}>
          {data.map((item) => (
            <Grid key={item.id} size={{ xs: 12, sm: 6, lg: 4 }}>
              <Card variant="outlined" sx={{ height: '100%' }}>
                <CardActionArea
                  onClick={() => navigate(`/catalog/${item.id}`)}
                  sx={{ height: '100%', alignItems: 'stretch' }}
                >
                  <CardContent>
                    <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={1}>
                      <Typography variant="subtitle1" fontWeight={600}>
                        {item.name}
                      </Typography>
                      {item.status !== 'Published' && (
                        <Chip label={item.status} size="small" color="warning" variant="outlined" />
                      )}
                    </Stack>

                    <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5, mb: 1.5 }}>
                      {item.shortDescription}
                    </Typography>

                    <Stack direction="row" gap={1} flexWrap="wrap">
                      {item.cost != null && (
                        <Chip label={formatCurrency(item.cost)} size="small" variant="outlined" />
                      )}

                      {item.estimatedDeliveryDays != null && (
                        <Chip
                          icon={<ScheduleOutlinedIcon />}
                          label={`~${item.estimatedDeliveryDays} working days`}
                          size="small"
                          variant="outlined"
                        />
                      )}

                      {item.requiresApproval && (
                        <Chip
                          icon={<HowToRegOutlinedIcon />}
                          label="Needs approval"
                          size="small"
                          color="warning"
                          variant="outlined"
                        />
                      )}
                    </Stack>
                  </CardContent>
                </CardActionArea>
              </Card>
            </Grid>
          ))}
        </Grid>
      )}
    </Box>
  );
}

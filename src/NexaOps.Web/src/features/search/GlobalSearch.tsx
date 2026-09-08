import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import {
  Autocomplete,
  Box,
  CircularProgress,
  InputAdornment,
  TextField,
  Typography,
} from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import { incidentsApi } from '@/api/incidents';
import type { IncidentListItem } from '@/api/types';
import { PriorityChip } from '@/components/StatusChips';
import { useDebounced } from '@/utils/useDebounced';

/**
 * Global search in the top bar.
 *
 * In this release it searches incidents, which is the only transactional module that exists.
 * The placeholder says so rather than implying coverage the product does not yet have; other
 * record types join the same control as their modules ship.
 */
export function GlobalSearch() {
  const navigate = useNavigate();
  const [input, setInput] = useState('');
  const debounced = useDebounced(input, 300);

  const results = useQuery({
    queryKey: ['search', debounced],
    queryFn: ({ signal }) =>
      incidentsApi.search({ search: debounced, pageSize: 8, sortBy: 'updatedAt' }, signal),
    // Two characters is the threshold the API enforces on pickers; the same restraint here
    // avoids firing a query on every first keystroke.
    enabled: debounced.trim().length >= 2,
  });

  return (
    <Autocomplete<IncidentListItem, false, false, true>
      freeSolo
      size="small"
      options={results.data?.items ?? []}
      filterOptions={(options) => options}
      inputValue={input}
      onInputChange={(_, value) => setInput(value)}
      onChange={(_, value) => {
        if (value && typeof value !== 'string') {
          navigate(`/incidents/${value.id}`);
          setInput('');
        }
      }}
      getOptionLabel={(option) => (typeof option === 'string' ? option : option.number)}
      loading={results.isFetching}
      noOptionsText={
        debounced.trim().length < 2 ? 'Type at least two characters' : 'No matching incidents'
      }
      renderOption={(props, option) => {
        const { key, ...rest } = props as { key: string } & Record<string, unknown>;

        return (
          <Box component="li" key={key} {...rest} sx={{ display: 'flex', gap: 1.5 }}>
            <Typography
              variant="body2"
              sx={{ fontFamily: 'monospace', fontWeight: 600, minWidth: 92 }}
            >
              {option.number}
            </Typography>

            <Typography variant="body2" noWrap sx={{ flex: 1 }}>
              {option.title}
            </Typography>

            <PriorityChip priority={option.priority} />
          </Box>
        );
      }}
      renderInput={(params) => (
        <TextField
          {...params}
          placeholder="Search incidents by number or summary"
          slotProps={{
            input: {
              ...params.InputProps,
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon fontSize="small" />
                </InputAdornment>
              ),
              endAdornment: (
                <>
                  {results.isFetching && <CircularProgress size={16} />}
                  {params.InputProps.endAdornment}
                </>
              ),
            },
          }}
        />
      )}
      sx={{ width: '100%' }}
    />
  );
}

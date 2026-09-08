import { Box, Button, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';

export function NotFoundPage() {
  return (
    <Box sx={{ textAlign: 'center', py: 10 }}>
      <Typography variant="h1" sx={{ fontSize: '4rem', color: 'text.disabled', mb: 1 }}>
        404
      </Typography>

      <Typography variant="h2" gutterBottom>
        That page does not exist
      </Typography>

      <Typography variant="body1" color="text.secondary" sx={{ mb: 3 }}>
        The link may be out of date, or the record may have been archived.
      </Typography>

      <Button component={RouterLink} to="/" variant="contained">
        Back to the service desk
      </Button>
    </Box>
  );
}

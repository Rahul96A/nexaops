import type { ReactNode } from 'react';
import { Box, Breadcrumbs, Link as MuiLink, Stack, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';

export interface Crumb {
  label: string;
  to?: string;
}

/** The standard page heading: breadcrumbs, title, optional subtitle, and page actions. */
export function PageHeader({
  title,
  subtitle,
  crumbs,
  actions,
}: {
  title: ReactNode;
  subtitle?: ReactNode;
  crumbs?: Crumb[];
  actions?: ReactNode;
}) {
  return (
    <Box sx={{ mb: 3 }}>
      {crumbs && crumbs.length > 0 && (
        <Breadcrumbs sx={{ mb: 1 }} aria-label="Breadcrumb">
          {crumbs.map((crumb) =>
            crumb.to ? (
              <MuiLink
                key={crumb.label}
                component={RouterLink}
                to={crumb.to}
                underline="hover"
                color="text.secondary"
                variant="body2"
              >
                {crumb.label}
              </MuiLink>
            ) : (
              <Typography key={crumb.label} variant="body2" color="text.primary">
                {crumb.label}
              </Typography>
            ),
          )}
        </Breadcrumbs>
      )}

      <Stack
        direction={{ xs: 'column', sm: 'row' }}
        spacing={2}
        alignItems={{ xs: 'flex-start', sm: 'center' }}
        justifyContent="space-between"
      >
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="h1" component="h1" sx={{ lineHeight: 1.25 }}>
            {title}
          </Typography>

          {subtitle && (
            <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
              {subtitle}
            </Typography>
          )}
        </Box>

        {actions && (
          <Stack direction="row" spacing={1} sx={{ flexShrink: 0 }}>
            {actions}
          </Stack>
        )}
      </Stack>
    </Box>
  );
}

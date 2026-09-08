import { useState, type ReactNode } from 'react';
import { Link as RouterLink, NavLink, Outlet, useNavigate } from 'react-router-dom';
import {
  AppBar,
  Avatar,
  Box,
  Chip,
  Divider,
  Drawer,
  IconButton,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  ListSubheader,
  Menu,
  MenuItem,
  Toolbar,
  Tooltip,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material';
import MenuIcon from '@mui/icons-material/Menu';
import DashboardOutlinedIcon from '@mui/icons-material/DashboardOutlined';
import MenuBookOutlinedIcon from '@mui/icons-material/MenuBookOutlined';
import EventAvailableOutlinedIcon from '@mui/icons-material/EventAvailableOutlined';
import TroubleshootOutlinedIcon from '@mui/icons-material/TroubleshootOutlined';
import StorefrontOutlinedIcon from '@mui/icons-material/StorefrontOutlined';
import HowToRegOutlinedIcon from '@mui/icons-material/HowToRegOutlined';
import ConfirmationNumberOutlinedIcon from '@mui/icons-material/ConfirmationNumberOutlined';
import AssignmentIndOutlinedIcon from '@mui/icons-material/AssignmentIndOutlined';
import HistoryOutlinedIcon from '@mui/icons-material/HistoryOutlined';
import HubOutlinedIcon from '@mui/icons-material/HubOutlined';
import DevicesOtherOutlinedIcon from '@mui/icons-material/DevicesOtherOutlined';
import LogoutOutlinedIcon from '@mui/icons-material/LogoutOutlined';
import DarkModeOutlinedIcon from '@mui/icons-material/DarkModeOutlined';
import LightModeOutlinedIcon from '@mui/icons-material/LightModeOutlined';
import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import { useAuth } from '@/auth/useAuth';
import { Permissions } from '@/auth/permissions';
import { initials } from '@/utils/format';
import { NotificationBell } from '@/features/notifications/NotificationBell';
import { GlobalSearch } from '@/features/search/GlobalSearch';
import { AiAssistantButton } from '@/features/ai/AiAssistantButton';

const DRAWER_WIDTH = 248;

interface NavItem {
  label: string;
  to: string;
  icon: ReactNode;
  /** Hidden when the user does not hold this permission. */
  permission?: string;
  /** Shown but disabled, with an explanation, for modules that ship in a later phase. */
  comingSoon?: boolean;
}

interface NavSection {
  heading: string;
  items: NavItem[];
}

/**
 * The left navigation.
 *
 * Modules that are not built yet appear, greyed, with an explicit "later phase" marker. That
 * is a deliberate choice over hiding them: a prospect seeing the shape of the product is
 * useful, a prospect clicking a live-looking link into an empty page is not.
 */
const NAVIGATION: NavSection[] = [
  {
    heading: 'Work',
    items: [
      { label: 'Service desk', to: '/', icon: <DashboardOutlinedIcon /> },
      {
        label: 'Incidents',
        to: '/incidents',
        icon: <ConfirmationNumberOutlinedIcon />,
        permission: Permissions.incidentRead,
      },
      { label: 'My work', to: '/my-work', icon: <AssignmentIndOutlinedIcon /> },
    ],
  },
  {
    heading: 'Service management',
    items: [
      {
        label: 'Service catalogue',
        to: '/catalog',
        icon: <StorefrontOutlinedIcon />,
        permission: Permissions.catalogRead,
      },
      {
        label: 'Requests',
        to: '/requests',
        icon: <ConfirmationNumberOutlinedIcon />,
        permission: Permissions.requestRead,
      },
      {
        label: 'Approvals',
        to: '/approvals',
        icon: <HowToRegOutlinedIcon />,
        permission: Permissions.approvalAct,
      },
      {
        label: 'Problems',
        to: '/problems',
        icon: <TroubleshootOutlinedIcon />,
        permission: Permissions.problemRead,
      },
      {
        label: 'Changes',
        to: '/changes',
        icon: <EventAvailableOutlinedIcon />,
        permission: Permissions.changeRead,
      },
      {
        label: 'Knowledge',
        to: '/knowledge',
        icon: <MenuBookOutlinedIcon />,
        permission: Permissions.knowledgeRead,
      },
    ],
  },
  {
    heading: 'Assets and configuration',
    items: [
      {
        label: 'Configuration items',
        to: '/cmdb',
        icon: <HubOutlinedIcon />,
        permission: Permissions.cmdbRead,
      },
      {
        label: 'Assets',
        to: '/assets',
        icon: <DevicesOtherOutlinedIcon />,
        permission: Permissions.assetRead,
      },
    ],
  },
  {
    heading: 'Administration',
    items: [
      {
        label: 'Audit trail',
        to: '/audit',
        icon: <HistoryOutlinedIcon />,
        permission: Permissions.auditRead,
      },
      { label: 'Workflows', to: '/workflows', icon: <ConfirmationNumberOutlinedIcon />, comingSoon: true },
      { label: 'Reports', to: '/reports', icon: <ConfirmationNumberOutlinedIcon />, comingSoon: true },
      { label: 'Settings', to: '/settings', icon: <ConfirmationNumberOutlinedIcon />, comingSoon: true },
    ],
  },
];

export function AppShell({
  colorMode,
  onToggleColorMode,
}: {
  colorMode: 'light' | 'dark';
  onToggleColorMode: () => void;
}) {
  const theme = useTheme();
  const isDesktop = useMediaQuery(theme.breakpoints.up('lg'));
  const [mobileOpen, setMobileOpen] = useState(false);
  const [userMenuAnchor, setUserMenuAnchor] = useState<HTMLElement | null>(null);

  const { profile, signOut, hasPermission } = useAuth();
  const navigate = useNavigate();

  const environmentLabel = import.meta.env.VITE_ENVIRONMENT_LABEL;

  async function handleSignOut() {
    setUserMenuAnchor(null);
    await signOut();
    navigate('/sign-in', { replace: true });
  }

  const drawerContent = (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <Toolbar sx={{ px: 2.5 }}>
        <Box
          component={RouterLink}
          to="/"
          sx={{ display: 'flex', alignItems: 'center', gap: 1.25, textDecoration: 'none' }}
        >
          <Box
            aria-hidden
            sx={{
              width: 30,
              height: 30,
              borderRadius: '8px',
              background: 'linear-gradient(135deg, #1B4DFF 0%, #7A3BFF 100%)',
              display: 'grid',
              placeItems: 'center',
              color: '#fff',
              fontWeight: 800,
              fontSize: 15,
            }}
          >
            N
          </Box>

          <Box>
            <Typography variant="h4" component="span" sx={{ color: 'text.primary', lineHeight: 1 }}>
              NexaOps
            </Typography>
            <Typography variant="caption" component="div" color="text.secondary" sx={{ lineHeight: 1.3 }}>
              Service management
            </Typography>
          </Box>
        </Box>
      </Toolbar>

      <Divider />

      <Box sx={{ overflowY: 'auto', flex: 1, pb: 2 }}>
        {NAVIGATION.map((section) => {
          const visible = section.items.filter(
            (item) => !item.permission || hasPermission(item.permission),
          );

          if (visible.length === 0) {
            return null;
          }

          return (
            <List
              key={section.heading}
              dense
              subheader={
                <ListSubheader
                  disableSticky
                  sx={{
                    fontSize: '0.6875rem',
                    fontWeight: 700,
                    letterSpacing: '0.08em',
                    textTransform: 'uppercase',
                    color: 'text.secondary',
                    bgcolor: 'transparent',
                    lineHeight: '32px',
                  }}
                >
                  {section.heading}
                </ListSubheader>
              }
            >
              {visible.map((item) => (
                <NavigationItem key={item.to} item={item} onNavigate={() => setMobileOpen(false)} />
              ))}
            </List>
          );
        })}
      </Box>

      <Divider />

      <Box sx={{ p: 2 }}>
        <Typography variant="caption" color="text.secondary" display="block" noWrap>
          {profile?.tenantName}
        </Typography>
        <Typography variant="caption" color="text.disabled" display="block">
          {profile?.organizationName ?? profile?.tenantCode}
        </Typography>
      </Box>
    </Box>
  );

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'background.default' }}>
      <AppBar
        position="fixed"
        elevation={0}
        color="inherit"
        sx={{
          zIndex: (t) => t.zIndex.drawer + 1,
          borderBottom: 1,
          borderColor: 'divider',
          bgcolor: 'background.paper',
        }}
      >
        <Toolbar sx={{ gap: 1.5 }}>
          {!isDesktop && (
            <IconButton
              edge="start"
              onClick={() => setMobileOpen((open) => !open)}
              aria-label="Open navigation"
            >
              <MenuIcon />
            </IconButton>
          )}

          <Box sx={{ flex: 1, maxWidth: 560 }}>
            <GlobalSearch />
          </Box>

          <Box sx={{ flex: 1 }} />

          {environmentLabel && (
            <Chip
              label={environmentLabel}
              size="small"
              color="warning"
              variant="outlined"
              sx={{ display: { xs: 'none', sm: 'inline-flex' } }}
            />
          )}

          <AiAssistantButton />
          <NotificationBell />

          <Tooltip title={colorMode === 'light' ? 'Switch to dark theme' : 'Switch to light theme'}>
            <IconButton onClick={onToggleColorMode} aria-label="Toggle colour theme">
              {colorMode === 'light' ? <DarkModeOutlinedIcon /> : <LightModeOutlinedIcon />}
            </IconButton>
          </Tooltip>

          <Tooltip title={profile?.displayName ?? 'Account'}>
            <IconButton
              onClick={(event) => setUserMenuAnchor(event.currentTarget)}
              aria-label="Open account menu"
              sx={{ ml: 0.5 }}
            >
              <Avatar
                sx={{
                  width: 32,
                  height: 32,
                  fontSize: 13,
                  fontWeight: 700,
                  bgcolor: profile?.avatarColor ?? 'primary.main',
                }}
              >
                {initials(profile?.displayName)}
              </Avatar>
            </IconButton>
          </Tooltip>

          <Menu
            anchorEl={userMenuAnchor}
            open={Boolean(userMenuAnchor)}
            onClose={() => setUserMenuAnchor(null)}
            slotProps={{ paper: { sx: { minWidth: 260 } } }}
          >
            <Box sx={{ px: 2, py: 1.5 }}>
              <Typography variant="h5">{profile?.displayName}</Typography>
              <Typography variant="caption" color="text.secondary" display="block">
                {profile?.email}
              </Typography>
              <Typography variant="caption" color="text.secondary" display="block">
                {profile?.jobTitle}
              </Typography>

              {profile && profile.roles.length > 0 && (
                <Box sx={{ mt: 1, display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
                  {profile.roles.map((role) => (
                    <Chip key={role} label={role} size="small" variant="outlined" />
                  ))}
                </Box>
              )}
            </Box>

            <Divider />

            <MenuItem component={RouterLink} to="/change-password" onClick={() => setUserMenuAnchor(null)}>
              <ListItemIcon>
                <LockOutlinedIcon fontSize="small" />
              </ListItemIcon>
              Change password
            </MenuItem>

            <MenuItem onClick={handleSignOut}>
              <ListItemIcon>
                <LogoutOutlinedIcon fontSize="small" />
              </ListItemIcon>
              Sign out
            </MenuItem>
          </Menu>
        </Toolbar>
      </AppBar>

      <Box component="nav" sx={{ width: { lg: DRAWER_WIDTH }, flexShrink: { lg: 0 } }}>
        <Drawer
          variant={isDesktop ? 'permanent' : 'temporary'}
          open={isDesktop || mobileOpen}
          onClose={() => setMobileOpen(false)}
          ModalProps={{ keepMounted: true }}
          sx={{
            '& .MuiDrawer-paper': {
              width: DRAWER_WIDTH,
              boxSizing: 'border-box',
              borderRight: 1,
              borderColor: 'divider',
            },
          }}
        >
          {drawerContent}
        </Drawer>
      </Box>

      <Box
        component="main"
        sx={{
          flexGrow: 1,
          width: { lg: `calc(100% - ${DRAWER_WIDTH}px)` },
          minWidth: 0,
        }}
      >
        <Toolbar />
        <Box sx={{ p: { xs: 2, md: 3 }, maxWidth: 1600, mx: 'auto' }}>
          <Outlet />
        </Box>
      </Box>
    </Box>
  );
}

function NavigationItem({ item, onNavigate }: { item: NavItem; onNavigate: () => void }) {
  if (item.comingSoon) {
    return (
      <Tooltip title="Ships in a later phase" placement="right">
        <span>
          <ListItemButton disabled sx={{ mx: 1, borderRadius: 2 }}>
            <ListItemIcon sx={{ minWidth: 36 }}>{item.icon}</ListItemIcon>
            <ListItemText primary={item.label} primaryTypographyProps={{ fontSize: '0.875rem' }} />
            <Typography variant="caption" color="text.disabled">
              Later
            </Typography>
          </ListItemButton>
        </span>
      </Tooltip>
    );
  }

  return (
    <ListItemButton
      component={NavLink}
      to={item.to}
      end={item.to === '/'}
      onClick={onNavigate}
      sx={{
        mx: 1,
        borderRadius: 2,
        '&.active': {
          bgcolor: 'action.selected',
          color: 'primary.main',
          fontWeight: 700,
          '& .MuiListItemIcon-root': { color: 'primary.main' },
        },
      }}
    >
      <ListItemIcon sx={{ minWidth: 36 }}>{item.icon}</ListItemIcon>
      <ListItemText primary={item.label} primaryTypographyProps={{ fontSize: '0.875rem' }} />
    </ListItemButton>
  );
}

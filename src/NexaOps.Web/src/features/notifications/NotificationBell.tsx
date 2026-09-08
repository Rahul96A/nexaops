import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Badge,
  Box,
  Button,
  Divider,
  IconButton,
  List,
  ListItemButton,
  ListItemText,
  Popover,
  Tooltip,
  Typography,
} from '@mui/material';
import NotificationsNoneOutlinedIcon from '@mui/icons-material/NotificationsNoneOutlined';
import { notificationsApi } from '@/api/notifications';
import { formatRelative } from '@/utils/format';

const SEVERITY_COLOR = {
  Information: 'info.main',
  Success: 'success.main',
  Warning: 'warning.main',
  Critical: 'error.main',
} as const;

/** The notification bell. Polls for the unread count and opens a short list on click. */
export function NotificationBell() {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const unread = useQuery({
    queryKey: ['notifications', 'unread-count'],
    queryFn: ({ signal }) => notificationsApi.unreadCount(signal),
    refetchInterval: 45_000,
  });

  const list = useQuery({
    queryKey: ['notifications', 'recent'],
    queryFn: ({ signal }) => notificationsApi.list(false, 1, 12, signal),
    // Only fetched while the popover is open; there is no point polling a hidden list.
    enabled: Boolean(anchor),
  });

  const markRead = useMutation({
    mutationFn: (id: string) => notificationsApi.markRead(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['notifications'] });
    },
  });

  const markAllRead = useMutation({
    mutationFn: () => notificationsApi.markAllRead(),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['notifications'] });
    },
  });

  const count = unread.data?.count ?? 0;

  return (
    <>
      <Tooltip title={count > 0 ? `${count} unread notifications` : 'Notifications'}>
        <IconButton onClick={(event) => setAnchor(event.currentTarget)} aria-label="Notifications">
          <Badge badgeContent={count} color="error" max={99}>
            <NotificationsNoneOutlinedIcon />
          </Badge>
        </IconButton>
      </Tooltip>

      <Popover
        open={Boolean(anchor)}
        anchorEl={anchor}
        onClose={() => setAnchor(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }}
        slotProps={{ paper: { sx: { width: 380, maxHeight: 480 } } }}
      >
        <Box sx={{ px: 2, py: 1.5, display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
          <Typography variant="h4">Notifications</Typography>

          {count > 0 && (
            <Button size="small" onClick={() => markAllRead.mutate()} disabled={markAllRead.isPending}>
              Mark all read
            </Button>
          )}
        </Box>

        <Divider />

        {list.data && list.data.items.length === 0 && (
          <Box sx={{ p: 4, textAlign: 'center' }}>
            <Typography variant="body2" color="text.secondary">
              Nothing to catch up on.
            </Typography>
          </Box>
        )}

        <List dense disablePadding>
          {(list.data?.items ?? []).map((notification) => (
            <ListItemButton
              key={notification.id}
              onClick={() => {
                if (!notification.isRead) {
                  markRead.mutate(notification.id);
                }

                if (notification.actionUrl) {
                  navigate(notification.actionUrl);
                }

                setAnchor(null);
              }}
              sx={{
                alignItems: 'flex-start',
                borderLeft: 3,
                borderColor: notification.isRead
                  ? 'transparent'
                  : SEVERITY_COLOR[notification.severity],
                bgcolor: notification.isRead ? 'transparent' : 'action.hover',
              }}
            >
              <ListItemText
                primary={notification.title}
                secondary={
                  <>
                    <Typography variant="caption" color="text.secondary" display="block" noWrap>
                      {notification.body}
                    </Typography>
                    <Typography variant="caption" color="text.disabled">
                      {formatRelative(notification.createdAt)}
                    </Typography>
                  </>
                }
                primaryTypographyProps={{
                  variant: 'body2',
                  fontWeight: notification.isRead ? 500 : 700,
                  noWrap: true,
                }}
              />
            </ListItemButton>
          ))}
        </List>
      </Popover>
    </>
  );
}

import { api } from './client';
import type { AppNotification, AuditEvent, PagedResult } from './types';

export const notificationsApi = {
  list: (unreadOnly: boolean, page: number, pageSize: number, signal?: AbortSignal) =>
    api.get<PagedResult<AppNotification>>('/notifications', { unreadOnly, page, pageSize }, signal),

  unreadCount: (signal?: AbortSignal) =>
    api.get<{ count: number }>('/notifications/unread-count', undefined, signal),

  markRead: (id: string) => api.post<void>(`/notifications/${id}/read`),

  markAllRead: () => api.post<void>('/notifications/read-all'),
};

export const auditApi = {
  search: (params: Record<string, unknown>, signal?: AbortSignal) =>
    api.get<PagedResult<AuditEvent>>('/audit', params, signal),

  forRecord: (entityType: string, entityId: string, signal?: AbortSignal) =>
    api.get<AuditEvent[]>(`/audit/${entityType}/${entityId}`, undefined, signal),
};

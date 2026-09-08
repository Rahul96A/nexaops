import { api } from './client';
import type {
  AddCommentRequest,
  AssignIncidentRequest,
  ChangePriorityRequest,
  ChangeStatusRequest,
  CreateIncidentRequest,
  IncidentActivity,
  IncidentComment,
  IncidentDetail,
  IncidentListItem,
  IncidentSearchParams,
  PagedResult,
  ServiceDeskSummary,
  UpdateIncidentRequest,
} from './types';

export const incidentsApi = {
  search: (params: IncidentSearchParams, signal?: AbortSignal) =>
    api.get<PagedResult<IncidentListItem>>(
      '/incidents',
      params as unknown as Record<string, unknown>,
      signal,
    ),

  summary: (signal?: AbortSignal) =>
    api.get<ServiceDeskSummary>('/incidents/summary', undefined, signal),

  get: (id: string, signal?: AbortSignal) =>
    api.get<IncidentDetail>(`/incidents/${id}`, undefined, signal),

  getByNumber: (number: string, signal?: AbortSignal) =>
    api.get<IncidentDetail>(`/incidents/by-number/${encodeURIComponent(number)}`, undefined, signal),

  create: (request: CreateIncidentRequest) => api.post<IncidentDetail>('/incidents', request),

  update: (id: string, request: UpdateIncidentRequest) =>
    api.patch<IncidentDetail>(`/incidents/${id}`, request),

  assign: (id: string, request: AssignIncidentRequest) =>
    api.post<IncidentDetail>(`/incidents/${id}/assign`, request),

  changeStatus: (id: string, request: ChangeStatusRequest) =>
    api.post<IncidentDetail>(`/incidents/${id}/status`, request),

  changePriority: (id: string, request: ChangePriorityRequest) =>
    api.post<IncidentDetail>(`/incidents/${id}/priority`, request),

  declareMajor: (id: string, isMajorIncident: boolean, reason: string) =>
    api.post<IncidentDetail>(`/incidents/${id}/major`, { isMajorIncident, reason }),

  comments: (id: string, signal?: AbortSignal) =>
    api.get<IncidentComment[]>(`/incidents/${id}/comments`, undefined, signal),

  addComment: (id: string, request: AddCommentRequest) =>
    api.post<IncidentComment>(`/incidents/${id}/comments`, request),

  activity: (id: string, signal?: AbortSignal) =>
    api.get<IncidentActivity[]>(`/incidents/${id}/activity`, undefined, signal),

  archive: (id: string, reason: string) => api.delete<void>(`/incidents/${id}`, { reason }),
};

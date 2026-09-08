import { api } from './client';
import type {
  ChangeDetail,
  ChangeOutcome,
  ChangeSearchParams,
  ChangeStatus,
  ChangeSummary,
  ChangeSummaryCounts,
  PagedResult,
} from './types';

export const changesApi = {
  search: (params: ChangeSearchParams, signal?: AbortSignal) =>
    api.get<PagedResult<ChangeSummary>>(
      '/changes',
      params as unknown as Record<string, unknown>,
      signal,
    ),

  summary: (signal?: AbortSignal) =>
    api.get<ChangeSummaryCounts>('/changes/summary', undefined, signal),

  get: (id: string, signal?: AbortSignal) =>
    api.get<ChangeDetail>(`/changes/${id}`, undefined, signal),

  changeStatus: (id: string, request: { status: ChangeStatus; note?: string }) =>
    api.post<ChangeDetail>(`/changes/${id}/status`, request),

  submit: (id: string) => api.post<ChangeDetail>(`/changes/${id}/submit`, {}),

  review: (id: string, request: { outcome: ChangeOutcome; notes: string }) =>
    api.post<ChangeDetail>(`/changes/${id}/review`, request),

  cancel: (id: string, request: { reason?: string }) =>
    api.post<ChangeDetail>(`/changes/${id}/cancel`, request),
};

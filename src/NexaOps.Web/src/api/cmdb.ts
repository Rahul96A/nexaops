import { api } from './client';
import type { CiDetail, CiSummary, CmdbSummaryCounts, PagedResult } from './types';

export interface CiSearchParams {
  search?: string;
  type?: string;
  status?: string;
  criticality?: string;
  scope?: string;
  sortBy?: string;
  sortDescending?: boolean;
  page?: number;
  pageSize?: number;
}

export const cmdbApi = {
  search: (params: CiSearchParams, signal?: AbortSignal) =>
    api.get<PagedResult<CiSummary>>(
      '/cmdb/items',
      params as unknown as Record<string, unknown>,
      signal,
    ),

  summary: (signal?: AbortSignal) =>
    api.get<CmdbSummaryCounts>('/cmdb/summary', undefined, signal),

  get: (id: string, signal?: AbortSignal) =>
    api.get<CiDetail>(`/cmdb/items/${id}`, undefined, signal),
};

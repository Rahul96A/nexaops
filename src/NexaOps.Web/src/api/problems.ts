import { api } from './client';
import type {
  CreateProblemRequest,
  PagedResult,
  ProblemComment,
  ProblemDetail,
  ProblemSearchParams,
  ProblemStatus,
  ProblemSummary,
  ProblemSummaryCounts,
  RecordFindingsRequest,
} from './types';

export const problemsApi = {
  search: (params: ProblemSearchParams, signal?: AbortSignal) =>
    api.get<PagedResult<ProblemSummary>>(
      '/problems',
      params as unknown as Record<string, unknown>,
      signal,
    ),

  summary: (signal?: AbortSignal) =>
    api.get<ProblemSummaryCounts>('/problems/summary', undefined, signal),

  get: (id: string, signal?: AbortSignal) =>
    api.get<ProblemDetail>(`/problems/${id}`, undefined, signal),

  create: (request: CreateProblemRequest) => api.post<ProblemDetail>('/problems', request),

  recordFindings: (id: string, request: RecordFindingsRequest) =>
    api.post<ProblemDetail>(`/problems/${id}/findings`, request),

  changeStatus: (id: string, request: { status: ProblemStatus; note?: string }) =>
    api.post<ProblemDetail>(`/problems/${id}/status`, request),

  comments: (id: string, signal?: AbortSignal) =>
    api.get<ProblemComment[]>(`/problems/${id}/comments`, undefined, signal),

  addComment: (id: string, request: { body: string; kind: 'PublicComment' | 'WorkNote' }) =>
    api.post<ProblemComment>(`/problems/${id}/comments`, request),

  linkIncident: (id: string, incidentId: string) =>
    api.post<ProblemDetail>(`/problems/${id}/incidents`, { incidentId }),

  unlinkIncident: (id: string, incidentId: string) =>
    api.delete<ProblemDetail>(`/problems/${id}/incidents/${incidentId}`),
};

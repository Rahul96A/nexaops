import { api } from './client';
import type {
  ArticleDetail,
  ArticleSearchParams,
  ArticleStatus,
  ArticleSummary,
  KnowledgeSummaryCounts,
  PagedResult,
} from './types';

export const knowledgeApi = {
  search: (params: ArticleSearchParams, signal?: AbortSignal) =>
    api.get<PagedResult<ArticleSummary>>(
      '/knowledge',
      params as unknown as Record<string, unknown>,
      signal,
    ),

  summary: (signal?: AbortSignal) =>
    api.get<KnowledgeSummaryCounts>('/knowledge/summary', undefined, signal),

  get: (id: string, signal?: AbortSignal) =>
    api.get<ArticleDetail>(`/knowledge/${id}`, undefined, signal),

  changeStatus: (
    id: string,
    request: { status: ArticleStatus; reason?: string; reviewIntervalDays?: number },
  ) => api.post<ArticleDetail>(`/knowledge/${id}/status`, request),

  feedback: (id: string, request: { wasHelpful: boolean; comment?: string }) =>
    api.post<ArticleDetail>(`/knowledge/${id}/feedback`, request),
};

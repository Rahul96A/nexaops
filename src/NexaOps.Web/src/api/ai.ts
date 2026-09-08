import { api } from './client';
import type { AiAnswer, AiStatus, AiTurn } from './types';

export const aiApi = {
  /**
   * Whether AI is available in this environment, and which tools the caller may use.
   * The UI calls this before showing the assistant, so a user is never offered a button that
   * cannot work.
   */
  status: (signal?: AbortSignal) => api.get<AiStatus>('/ai/status', undefined, signal),

  ask: (question: string, history: AiTurn[]) => api.post<AiAnswer>('/ai/ask', { question, history }),
};

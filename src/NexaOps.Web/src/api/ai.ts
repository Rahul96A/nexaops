import { api } from './client';
import type {
  AiAnswer,
  AiStatus,
  AiTurn,
  Urgency,
  VirtualAgentActionResult,
  VirtualAgentReply,
} from './types';

export const aiApi = {
  /**
   * Whether AI is available in this environment, and which tools the caller may use.
   * The UI calls this before showing the assistant, so a user is never offered a button that
   * cannot work.
   */
  status: (signal?: AbortSignal) => api.get<AiStatus>('/ai/status', undefined, signal),

  ask: (question: string, history: AiTurn[]) => api.post<AiAnswer>('/ai/ask', { question, history }),
};

export const agentApi = {
  chat: (message: string, history: AiTurn[]) =>
    api.post<VirtualAgentReply>('/ai/agent/chat', { message, history }),

  /**
   * Acts on a proposal the person has accepted.
   *
   * The fields are sent as shown on screen rather than as a reference to something the server
   * remembered, so an edited title is the title that becomes the ticket.
   */
  confirm: (input: { kind: string; title: string; description: string; urgency: Urgency }) =>
    api.post<VirtualAgentActionResult>('/ai/agent/confirm', input),
};

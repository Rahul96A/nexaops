import { describe, expect, it } from 'vitest';
import { ApiError } from './client';
import type { ProblemDetails } from './types';

/**
 * How API failures are classified for the UI.
 *
 * The rule this enforces is that callers branch on the stable `code`, never on the message.
 * Messages are written for people and get reworded; a screen that switches behaviour on prose
 * breaks the next time someone improves the wording.
 */
describe('ApiError', () => {
  function problem(overrides: Partial<ProblemDetails> = {}): ProblemDetails {
    return {
      type: 'https://docs.nexaops.io/problems/forbidden',
      title: 'You do not have permission to perform this action.',
      detail: 'Contact your NexaOps administrator if you believe this is incorrect.',
      code: 'forbidden',
      correlationId: 'abc123',
      ...overrides,
    };
  }

  it('exposes the machine-readable code', () => {
    const error = new ApiError(403, problem());
    expect(error.code).toBe('forbidden');
  });

  it('reports an unknown code rather than undefined when the server omits one', () => {
    const error = new ApiError(500, { title: 'Something broke' });
    expect(error.code).toBe('unknown_error');
  });

  it('classifies the statuses the UI reacts to', () => {
    expect(new ApiError(401, problem({ code: 'unauthenticated' })).isUnauthorized).toBe(true);
    expect(new ApiError(403, problem()).isForbidden).toBe(true);
    expect(new ApiError(404, problem({ code: 'not_found' })).isNotFound).toBe(true);
    expect(new ApiError(409, problem({ code: 'concurrency.conflict' })).isConflict).toBe(true);
  });

  it('recognises an unconfigured AI environment specifically', () => {
    // This drives an explanatory panel rather than a generic error, so it must not be confused
    // with an AI provider that is configured but failing.
    const notConfigured = new ApiError(503, problem({ code: 'ai_not_configured' }));
    expect(notConfigured.isAiUnavailable).toBe(true);

    const providerFailure = new ApiError(503, problem({ code: 'internal_error' }));
    expect(providerFailure.isAiUnavailable).toBe(false);
  });

  it('surfaces field errors from a validation failure', () => {
    const error = new ApiError(
      400,
      problem({
        code: 'validation_failed',
        errors: {
          title: ['A short summary is required.'],
          description: ['A description is required.'],
        },
      }),
    );

    expect(error.isValidationError).toBe(true);
    expect(error.fieldErrors.title).toEqual(['A short summary is required.']);

    // The top-level detail on a validation failure is deliberately generic, so the user-facing
    // message is assembled from the field messages instead.
    expect(error.userMessage).toContain('A short summary is required.');
    expect(error.userMessage).toContain('A description is required.');
  });

  it('does not treat a 400 without field errors as a validation failure', () => {
    const error = new ApiError(400, problem({ code: 'malformed_request' }));
    expect(error.isValidationError).toBe(false);
  });

  it('uses the server detail as the user-facing message for other failures', () => {
    const error = new ApiError(
      422,
      problem({
        code: 'incident.invalid_transition',
        detail: 'An incident cannot move from New to Closed.',
      }),
    );

    expect(error.userMessage).toBe('An incident cannot move from New to Closed.');
  });

  it('carries the correlation id so support can find the request', () => {
    const error = new ApiError(500, problem({ code: 'internal_error', correlationId: 'trace-42' }));
    expect(error.problem.correlationId).toBe('trace-42');
  });

  it('is a real Error, so it survives being thrown and caught', () => {
    const error = new ApiError(403, problem());

    expect(error).toBeInstanceOf(Error);
    expect(error.name).toBe('ApiError');
    expect(() => {
      throw error;
    }).toThrow();
  });
});

import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, request } from './client';

afterEach(() => vi.unstubAllGlobals());

describe('API problem response validation', () => {
  it.each([null, 'not a problem', { detail: 42, errors: { Reason: 'invalid', Actor: [false] } }])(
    'handles malformed error documents safely (%j)', async (body) => {
      vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(
        new Response(JSON.stringify(body), { status: 400, statusText: 'Bad Request' })));
      try {
        await request<unknown>('/tickets');
        throw new Error('Expected API rejection');
      } catch (error) {
        expect(error).toBeInstanceOf(ApiError);
        if (!(error instanceof ApiError)) throw error;
        expect(error.message).toBe('Bad Request');
        expect(error.fieldErrors).toEqual({});
      }
    });

  it('preserves valid messages and filters malformed validation fields', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(new Response(JSON.stringify({
      detail: 'Reason rejected', errors: { Reason: ['Too short'], Actor: null },
    }), { status: 400 })));
    try {
      await request<unknown>('/tickets');
      throw new Error('Expected API rejection');
    } catch (error) {
      if (!(error instanceof ApiError)) throw error;
      expect(error.message).toBe('Reason rejected');
      expect(error.fieldErrors).toEqual({ Reason: ['Too short'] });
    }
  });
});

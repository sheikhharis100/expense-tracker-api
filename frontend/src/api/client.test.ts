import { describe, expect, it } from 'vitest';
import { readError } from './client';

const response = (status: number, body: string) => new Response(body || null, { status });

describe('readError', () => {
  it('shows the plain message from { error }', async () => {
    expect(await readError(response(400, '{"error":"Amount must be greater than zero."}'))).toEqual({
      message: 'Amount must be greater than zero.',
      detail: undefined,
    });
  });

  it('keeps the latest copy that comes with a conflict', async () => {
    const result = await readError(response(409, '{"error":"Changed on another device.","current":{"id":7}}'));
    expect(result).toEqual({ message: 'Changed on another device.', detail: { id: 7 } });
  });

  it('prefers the plain message over the field list in validation problems', async () => {
    const body = JSON.stringify({
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: { $: ['The request body has the wrong shape.'], Status: ['Status must be Paid or Planned.'] },
      error: 'Status must be Paid or Planned.',
    });
    expect((await readError(response(400, body))).message).toBe('Status must be Paid or Planned.');
  });

  it('still reads older ProblemDetails, plain strings and text', async () => {
    expect((await readError(response(400, '{"errors":{"Name":["Name is required."]}}'))).message).toBe('Name is required.');
    expect((await readError(response(400, '"A category with this name already exists"'))).message).toBe('A category with this name already exists');
    expect((await readError(response(502, 'Bad gateway'))).message).toBe('Bad gateway');
  });

  it('explains empty error responses', async () => {
    expect((await readError(response(413, ''))).message).toBe('That upload is too large.');
    expect((await readError(response(401, ''))).message).toBe('Please sign in again.');
    expect((await readError(response(500, ''))).message).toBe('Request failed (500)');
  });
});

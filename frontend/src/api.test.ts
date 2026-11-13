import { describe, expect, it } from 'vitest'
import { apiFetch } from '@/api'
import { client } from '@/client/client.gen'

describe('apiFetch', () => {
  it('is the fetch of the generated client from the start, without any setup call', () => {
    expect(client.getConfig().fetch).toBe(apiFetch)
  })
})

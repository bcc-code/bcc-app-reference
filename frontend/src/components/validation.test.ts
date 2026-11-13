import { describe, expect, it } from 'vitest'
import { errorMessages, isRequired } from './validation'

describe('form validation', () => {
  it('marks a field required by a required rule, or a requiredIf whose condition holds', () => {
    expect(isRequired({ $errors: [], required: { $params: { type: 'required' } } })).toBe(true)
    expect(isRequired({ $errors: [], required: { $params: { type: 'requiredIf', prop: () => true } } })).toBe(true)
    expect(isRequired({ $errors: [], required: { $params: { type: 'requiredIf', prop: () => false } } })).toBe(false)
    expect(isRequired({ $errors: [] })).toBe(false)
    expect(isRequired(undefined)).toBe(false)
  })

  it("shows each failed rule's message, and the server's only when there is none of its own", () => {
    const failed = { $errors: [{ $uid: 'title-required', $validator: 'required' }] }
    expect(errorMessages(failed, true)).toEqual([{ id: 'title-required', key: 'form.alerts.required' }])
    expect(errorMessages({ $errors: [] }, true)).toEqual([{ id: 'server', key: 'form.alerts.checkField' }])
    expect(errorMessages(undefined, false)).toEqual([])
  })
})

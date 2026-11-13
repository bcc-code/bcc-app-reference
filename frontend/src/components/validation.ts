/**
 * The part of a Vuelidate field result the form wrappers read: its errors, and whether it has a required rule that is on
 * (required, or requiredIf with its condition true). Structural, so a field result of any form fits.
 */
export interface FieldValidation {
  readonly $errors: readonly { $uid: string; $validator: string }[]
  readonly required?: object
}

type RequiredRule = { $params?: { type?: string; prop?: unknown } }

export function isRequired(validation: FieldValidation | undefined): boolean {
  const params = (validation?.required as RequiredRule | undefined)?.$params
  if (!params) return false
  return params.type === 'required' || (typeof params.prop === 'function' && Boolean(params.prop()))
}

/** One message per failed rule, under form.alerts as today; a field the server refused shows checkField. */
export function errorMessages(validation: FieldValidation | undefined, serverInvalid: boolean): { id: string; key: string }[] {
  const own = (validation?.$errors ?? []).map((e) => ({ id: e.$uid, key: `form.alerts.${e.$validator}` }))
  return own.length === 0 && serverInvalid ? [{ id: 'server', key: 'form.alerts.checkField' }] : own
}

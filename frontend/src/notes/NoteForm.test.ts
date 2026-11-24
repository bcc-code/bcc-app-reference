import { createApp, nextTick } from 'vue'
import { describe, expect, it, vi } from 'vitest'
import { installBccComponentLibrary } from '@/csp'
import { createTemplateI18n } from '@/i18n'
import NoteForm from './NoteForm.vue'
import type { NoteDraft } from './queries'

/** The form mounted with the real plugins, as the app installs them, and the drafts it emits. */
function mountForm(props: { error?: unknown; initial?: NoteDraft } = {}) {
  const submitted: NoteDraft[] = []
  const element = document.createElement('div')
  document.body.append(element)
  const app = createApp(NoteForm, {
    submitLabel: 'Save',
    pending: false,
    error: undefined,
    ...props,
    onSubmit: (draft: NoteDraft) => submitted.push(draft),
  })
  installBccComponentLibrary(app, undefined)
  app.use(createTemplateI18n('en'))
  app.mount(element)
  return {
    element,
    submitted,
    title: () => element.querySelector('input')!,
    body: () => element.querySelector('textarea')!,
    type(field: HTMLInputElement | HTMLTextAreaElement, value: string) {
      field.value = value
      field.dispatchEvent(new Event('input'))
    },
    async submit() {
      element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }))
      await nextTick()
    },
    unmount: () => {
      app.unmount()
      element.remove()
    },
  }
}

describe('NoteForm', () => {
  it('starts from the note it is given and submits what the user typed', async () => {
    const form = mountForm({ initial: { title: 'First', body: 'Text' } })
    form.type(form.title(), 'Second')
    await form.submit()
    await vi.waitFor(() => expect(form.submitted).toEqual([{ title: 'Second', body: 'Text' }]))
    form.unmount()
  })

  it('has labelled fields', () => {
    const form = mountForm()
    const label = form.element.querySelector('label[for]')!
    expect(label.textContent).toBe('Title')
    expect(form.title().id).toBe(label.getAttribute('for'))
    form.unmount()
  })

  it('does not submit without a title and says so', async () => {
    const form = mountForm({ initial: { title: '', body: 'Text' } })
    await form.submit()
    await vi.waitFor(() => expect(form.element.textContent).toContain('This field is required.'))
    expect(form.submitted).toEqual([])
    form.unmount()
  })

  it('does not submit a title or text that is too long', async () => {
    const form = mountForm({ initial: { title: 'x'.repeat(201), body: 'y'.repeat(10_001) } })
    await form.submit()
    await vi.waitFor(() => expect(form.element.textContent).toContain('must not exceed 200 characters'))
    expect(form.element.textContent).toContain('must not exceed 10 000 characters')
    expect(form.submitted).toEqual([])
    form.unmount()
  })

  it('shows the translated message of a problem from the server', () => {
    const form = mountForm({ error: { status: 400, errors: { title: ['errors.note.titleRequired'] } } })
    expect(form.element.querySelector('[role="alert"]')?.textContent).toBe('Enter a title.')
    form.unmount()
  })
})

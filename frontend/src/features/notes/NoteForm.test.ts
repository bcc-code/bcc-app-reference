import { BccComponentLibrary } from '@bcc-code/component-library-vue'
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { createTemplateI18n } from '@/i18n'
import NoteForm from './NoteForm.vue'

function mountForm(props: { error?: unknown; initial?: { title: string; body: string } } = {}) {
  return mount(NoteForm, {
    props: { submitLabel: 'Save', pending: false, error: undefined, ...props },
    global: { plugins: [BccComponentLibrary, createTemplateI18n('en')] },
  })
}

describe('NoteForm', () => {
  it('starts from the note it is given and submits what the user typed', async () => {
    const form = mountForm({ initial: { title: 'First', body: 'Text' } })
    await form.find('input[name="title"]').setValue('Second')
    await form.find('form').trigger('submit')
    expect(form.emitted('submit')).toEqual([[{ title: 'Second', body: 'Text' }]])
  })

  it('has labelled fields', () => {
    const form = mountForm()
    expect(form.find('label[for]').text()).toBe('Title')
    const id = form.find('label[for]').attributes('for')
    expect(form.find('input[name="title"]').attributes('id')).toBe(id)
  })

  it('shows the translated message of a problem from the server', () => {
    const form = mountForm({ error: { status: 400, errors: { title: ['errors.note.titleRequired'] } } })
    expect(form.get('[role="alert"]').text()).toBe('Enter a title.')
  })
})

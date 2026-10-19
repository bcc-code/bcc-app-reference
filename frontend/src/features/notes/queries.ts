import { queryOptions, useMutation, useQueryClient } from '@tanstack/vue-query'
import { archiveNote, createNote, editNote, getNote, listNotes, type NoteResponse } from '@/client'

/** One key per resource, so a mutation invalidates the right queries. */
const notesKeys = {
  all: ['notes'] as const,
  list: () => [...notesKeys.all, 'list'] as const,
  detail: (noteId: string) => [...notesKeys.all, 'detail', noteId] as const,
}

export const notesQueryOptions = () =>
  queryOptions({
    queryKey: notesKeys.list(),
    queryFn: async ({ signal }) => (await listNotes({ signal })).data,
  })

export const noteQueryOptions = (noteId: string) =>
  queryOptions({
    queryKey: notesKeys.detail(noteId),
    queryFn: async ({ signal }) => (await getNote({ path: { noteId }, signal })).data,
  })

export interface NoteDraft {
  title: string
  body: string
}

export function useCreateNote() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (draft: NoteDraft) => (await createNote({ body: draft })).data,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: notesKeys.list() }),
  })
}

/** `version` is the one the user saw; the server answers 409 when the note has moved on. */
export function useEditNote(noteId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: NoteDraft & { version: number }) => (await editNote({ path: { noteId }, body: input })).data,
    onSuccess: (note: NoteResponse) => {
      queryClient.setQueryData(notesKeys.detail(noteId), note)
      return queryClient.invalidateQueries({ queryKey: notesKeys.list() })
    },
  })
}

export function useArchiveNote() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (note: Pick<NoteResponse, 'id' | 'version'>) =>
      archiveNote({ path: { noteId: note.id }, body: { version: note.version } }),
    // A stale version (409) means the list is out of date too, so it is refreshed whatever the outcome.
    onSettled: () => queryClient.invalidateQueries({ queryKey: notesKeys.all }),
  })
}

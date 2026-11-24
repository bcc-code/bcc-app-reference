import { queryOptions, useMutation, useQueryClient } from '@tanstack/vue-query'
import { NotesClient, type EditNoteResponse, type GetNoteResponse } from '@/client'

export interface NoteDraft {
  title: string
  body: string
}

export const notesQueries = {
  /** Every query of the notes, to invalidate after a change. */
  all: () => ['notes'] as const,
  list: () =>
    queryOptions({
      queryKey: [...notesQueries.all(), 'list'],
      queryFn: async ({ signal }) => (await NotesClient.listNotes({ signal })).data.notes,
    }),
  detail: (noteId: string) =>
    queryOptions({
      queryKey: [...notesQueries.all(), 'detail', noteId],
      queryFn: async ({ signal }) => (await NotesClient.getNote({ path: { noteId }, signal })).data,
    }),
}

export function useCreateNote() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (draft: NoteDraft) => (await NotesClient.createNote({ body: draft })).data,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: notesQueries.list().queryKey }),
  })
}

/** `version` is the one the user saw; the server answers 409 when the note has moved on. */
export function useEditNote(noteId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: NoteDraft & { version: number }) =>
      (await NotesClient.editNote({ path: { noteId }, body: input })).data,
    onSuccess: (note: EditNoteResponse) => {
      queryClient.setQueryData(notesQueries.detail(noteId).queryKey, note)
      return queryClient.invalidateQueries({ queryKey: notesQueries.list().queryKey })
    },
  })
}

export function useArchiveNote() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (note: Pick<GetNoteResponse, 'id' | 'version'>) =>
      NotesClient.archiveNote({ path: { noteId: note.id }, body: { version: note.version } }),
    // A stale version (409) means the list is out of date too, so it is refreshed whatever the outcome.
    onSettled: () => queryClient.invalidateQueries({ queryKey: notesQueries.all() }),
  })
}

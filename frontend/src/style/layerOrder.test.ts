// @vitest-environment node
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { LAYER_ORDER } from './layerOrder'

describe('cascade layer order', () => {
  it('main.css declares the same order the app re-asserts at runtime', () => {
    const css = readFileSync(new URL('./main.css', import.meta.url), 'utf8')
    expect(css.match(/^@layer [^{]*?;/m)?.[0]).toBe(LAYER_ORDER)
  })
})

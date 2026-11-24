// Removes the Postgres volumes of isolated Aspire runs (`aspire start --isolated`) whose worktree no longer exists. Each isolated
// run keeps its database in `bcc-template-<slug>-postgres`, where the slug comes from the checkout folder's name (RunMode in
// api/AppHost); the volume outlives the worktree unless something removes it. The sole instance's `bcc-template-postgres` is
// never touched.
//
// "Live" means a worktree of this clone. An isolated run of a separate clone has a slug this clone does not know, so its volume
// looks orphaned here. The SessionStart hook in .claude/settings.json therefore only lists (--dry-run); remove by hand after
// reading the list:
//   node tools/worktree-cleanup/prune-volumes.mjs [--dry-run]
// Prints nothing and exits 0 when git or Docker is not available. A volume a container still uses is left alone (docker refuses
// to remove it).
import { execFileSync } from 'node:child_process'
import { existsSync } from 'node:fs'
import { basename } from 'node:path'

const prefix = 'bcc-template-'
const dryRun = process.argv.includes('--dry-run')

function run(command, args) {
  return execFileSync(command, args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'], timeout: 10_000 })
}

// The same rules as RunMode.SlugOfCheckout: lower case, anything but an ASCII letter or digit becomes '-', trimmed, at most 40
// characters.
function slugOf(folderName) {
  let slug = folderName.toLowerCase().replace(/[^a-z0-9]/g, '-').replace(/^-+|-+$/g, '')
  if (slug.length > 40) slug = slug.slice(0, 40).replace(/-+$/, '')
  return slug.length > 0 ? slug : 'isolated'
}

let worktrees
try {
  worktrees = run('git', ['worktree', 'list', '--porcelain'])
    .split('\n')
    .filter((line) => line.startsWith('worktree '))
    .map((line) => line.slice('worktree '.length).trim())
} catch {
  process.exit(0)
}

// A worktree git still lists but whose folder is gone counts as gone.
const live = new Set(worktrees.filter((path) => existsSync(path)).map((path) => slugOf(basename(path))))

let volumes
try {
  volumes = run('docker', ['volume', 'ls', '--quiet', '--filter', `name=${prefix}`]).split('\n').map((v) => v.trim())
} catch {
  process.exit(0)
}

for (const volume of volumes) {
  const match = new RegExp(`^${prefix}(.+)-postgres$`).exec(volume)
  if (!match || volume === `${prefix}postgres` || live.has(match[1])) continue

  if (dryRun) {
    console.log(`would remove ${volume}`)
    continue
  }
  try {
    run('docker', ['volume', 'rm', volume])
    console.log(`removed ${volume} (its worktree is gone)`)
  } catch {
    // In use by a container, or removed meanwhile.
  }
}

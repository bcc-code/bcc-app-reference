// SessionStart hook for Claude Code cloud sessions (claude.ai/code): puts the toolchains from scripts/cloud-setup.sh on the
// PATH and restores the repository's packages, so Claude can build, test and run the app without a first round of setup.
// Does nothing outside the cloud (CLAUDE_CODE_REMOTE is never "true" locally). The setup script cannot do the restore: it runs
// before the checkout exists, and its cache would keep stale packages after a lock file changes.
//
// The restore is skipped when the lock files are unchanged since the last successful one (a hash in .git, which is not
// tracked). Whatever this prints goes into Claude's context, so it prints only what Claude needs to act on. It always exits 0:
// a failed restore should not stop the session, and Claude can run the commands again and read the errors.
import { execFileSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { appendFileSync, existsSync, readFileSync, writeFileSync } from 'node:fs'
import { delimiter, join } from 'node:path'

if (process.env.CLAUDE_CODE_REMOTE !== 'true') process.exit(0)

const root = process.env.CLAUDE_PROJECT_DIR ?? process.cwd()
const nodeBin = '/opt/node24/bin' // NODE_DIR in scripts/cloud-setup.sh
const dotnetRoot = '/usr/share/dotnet' // DOTNET_DIR in scripts/cloud-setup.sh

// CLAUDE_ENV_FILE carries variables into every later command of the session; the image's PATH has Node 22 first.
const env = { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: '1' }
if (existsSync(nodeBin)) env.PATH = `${nodeBin}${delimiter}${env.PATH}`
if (existsSync(dotnetRoot)) env.DOTNET_ROOT = dotnetRoot
if (process.env.CLAUDE_ENV_FILE) {
  const lines = [`export DOTNET_CLI_TELEMETRY_OPTOUT=1`]
  if (existsSync(nodeBin)) lines.push(`export PATH="${nodeBin}:$PATH"`)
  if (existsSync(dotnetRoot)) lines.push(`export DOTNET_ROOT="${dotnetRoot}"`)
  appendFileSync(process.env.CLAUDE_ENV_FILE, lines.join('\n') + '\n')
}

function run(command, args) {
  return execFileSync(command, args, { cwd: root, env, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], timeout: 300_000 })
}

try {
  run('dotnet', ['--version'])
} catch {
  console.log(
    'Cloud session: the .NET SDK is not installed, so the cloud environment has not run scripts/cloud-setup.sh. ' +
      'Builds and tests will fail until a developer sets it up (docs/development/non-developers.md).',
  )
  process.exit(0)
}

const lockFiles = run('git', ['ls-files', '*packages.lock.json', 'frontend/pnpm-lock.yaml', '.config/dotnet-tools.json'])
  .split('\n')
  .filter(Boolean)
  .sort()
const hash = createHash('sha256')
for (const file of lockFiles) hash.update(file).update(readFileSync(join(root, file)))
const digest = hash.digest('hex')
const marker = join(root, '.git', 'cloud-session-restored')
if (existsSync(marker) && readFileSync(marker, 'utf8').trim() === digest) process.exit(0)

const steps = [
  // dotnet/sdk#53783: on an empty NuGet cache (every new cloud VM) the first tool restore fails and the second succeeds. CI
  // retries the same way (.github/actions/setup/action.yml).
  { command: 'dotnet', args: ['tool', 'restore'], attempts: 2 },
  { command: 'dotnet', args: ['restore', 'BccCode.Template.slnx', '--locked-mode'], attempts: 1 },
  { command: 'pnpm', args: ['--dir', 'frontend', 'install', '--frozen-lockfile'], attempts: 1 },
]
for (const { command, args, attempts } of steps) {
  for (let attempt = 1; attempt <= attempts; attempt++) {
    try {
      run(command, args)
      break
    } catch (error) {
      if (attempt < attempts) continue
      const output = `${error.stdout ?? ''}${error.stderr ?? ''}`.trim().split('\n').slice(-20).join('\n')
      console.log(`Cloud session: \`${command} ${args.join(' ')}\` failed, so the packages are not restored.\n${output}`)
      process.exit(0)
    }
  }
}
writeFileSync(marker, digest)
console.log('Cloud session: .NET tools, NuGet packages and frontend packages are restored.')

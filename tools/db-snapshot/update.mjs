// Regenerates the committed database snapshot the AppHost restores into a new Postgres
// (api/BccCode.Template.TestData/Snapshots/, see docs/development/database-and-migrations.md):
//   node tools/db-snapshot/update.mjs
//
// Starts a throwaway Postgres, runs the schema step (jasper db-apply: the EF Core migrations, then Wolverine's tables) and the
// seeder once against it, and writes the result as small files (see write-snapshot.mjs). Run it after a migration or a change
// to the seeder. Running it twice gives identical files. Needs Docker and the .NET SDK.
import { spawn, spawnSync } from 'node:child_process'
import { randomBytes } from 'node:crypto'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { writeSnapshot } from './write-snapshot.mjs'

// The image the AppHost's AddPostgres uses (DatabaseResources.cs, WithImageTag("18")), so the dump comes from the server
// version the AppHost restores it into.
const IMAGE = 'docker.io/library/postgres:18'
const DATABASE = 'template'
const PASSWORD = randomBytes(12).toString('hex')

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..')
const snapshotDir = join(root, 'api', 'BccCode.Template.TestData', 'Snapshots')
const container = `template-snapshot-${randomBytes(4).toString('hex')}`

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

function docker(...args) {
  const result = spawnSync('docker', args, { encoding: 'utf8' })
  if (result.status !== 0) throw new Error(`docker ${args.join(' ')} failed: ${result.stderr || result.stdout}`)
  return result.stdout.trim()
}

// Runs a command to completion and fails with the tail of its output.
function runStep(label, args, env) {
  return new Promise((resolveStep, reject) => {
    const child = spawn('dotnet', args, { cwd: root, env: { ...process.env, ...env }, stdio: ['ignore', 'pipe', 'pipe'] })
    const tail = []
    const keep = (data) => {
      tail.push(...data.toString().split('\n'))
      tail.splice(0, Math.max(0, tail.length - 40))
    }
    child.stdout.on('data', keep)
    child.stderr.on('data', keep)
    child.on('exit', (code) =>
      code === 0 ? resolveStep() : reject(new Error(`${label} exited with code ${code}:\n${tail.join('\n')}`)),
    )
  })
}

async function waitFor(what, timeoutMs, check) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if (await check()) return
    await sleep(1000)
  }
  throw new Error(`Timed out waiting for ${what}`)
}

async function run() {
  console.log(`Starting ${IMAGE}`)
  docker('run', '-d', '--rm', '--name', container, '-e', `POSTGRES_PASSWORD=${PASSWORD}`, '-p', '127.0.0.1::5432', IMAGE)

  // pg_isready over TCP: during initialisation the server only listens on its socket.
  await waitFor('postgres', 120_000, () => spawnSync('docker', ['exec', container, 'pg_isready', '-h', '127.0.0.1']).status === 0)
  docker('exec', container, 'psql', '-U', 'postgres', '-v', 'ON_ERROR_STOP=1', '-c', `CREATE DATABASE "${DATABASE}"`)

  const hostPort = docker('port', container, '5432/tcp').split('\n')[0].split(':').pop()
  const env = {
    // What WithReference(database) hands the projects in the AppHost.
    [`ConnectionStrings__${DATABASE}`]: `Host=127.0.0.1;Port=${hostPort};Username=postgres;Password=${PASSWORD};Database=${DATABASE}`,
    // The seeder refuses to run outside a disposable environment (SeedingGate).
    ASPNETCORE_ENVIRONMENT: 'Development',
    DOTNET_ENVIRONMENT: 'Development',
  }

  console.log('Applying the schema (jasper db-apply)')
  await runStep('db-apply', ['run', '--project', 'api/BccCode.Template.Api', '--no-launch-profile', '--', 'jasper', 'db-apply'], env)

  console.log('Seeding once')
  await runStep('seeder', ['run', '--project', 'api/BccCode.Template.TestData', '--no-launch-profile'], { ...env, Seeder__Once: 'true' })

  console.log(`Writing ${snapshotDir}`)
  await writeSnapshot({ container, database: DATABASE, target: snapshotDir })
}

try {
  await run()
  console.log('Done')
} catch (error) {
  console.error(error.message ?? error)
  process.exitCode = 1
} finally {
  spawnSync('docker', ['rm', '-f', container], { stdio: 'ignore' })
}

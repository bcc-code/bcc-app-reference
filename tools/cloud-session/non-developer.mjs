// SessionStart hook: when the person in the session is not a developer, tells Claude to work by the `non-developer` skill.
// The switch is the environment variable BCC_NON_DEVELOPER=true, set on the Claude Code cloud environment that non-developers
// use (docs/development/non-developers.md), so developers in the same repository get the usual behaviour. What this prints
// goes into Claude's context.
if (process.env.BCC_NON_DEVELOPER === 'true') {
  console.log(
    [
      'The person in this session is not a developer. They describe what they want in their own words and judge the result',
      'by looking at it; they do not read code and do not run commands.',
      'Before anything else, read .claude/skills/non-developer/SKILL.md and follow it for the whole session.',
    ].join(' '),
  )
}

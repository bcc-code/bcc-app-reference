# Code design principles

These principles guide how we write and structure code day to day: keep it readable, keep it simple, do not build for needs that do not exist, and record why. Several are made concrete by standards, for example [25 Code Quality Guards](../standards/25-code-quality-guards.md) and [23 Documentation and Decisions](../standards/23-documentation-and-decisions.md).

## Comment the Why, Not the What

Code already says what it does. Comments should explain why it does it: the context a future reader cannot recover from the code alone.

- Document intent, trade-offs, and non-obvious constraints: why this order, why this workaround, why not the obvious alternative.
- Do not restate the code (`// increment i`). It rots and adds noise.
- If a comment is needed to explain what the code does, first try to make the code clearer with better names and smaller functions.
- Link to the source of a decision (ticket, ADR, RFC) when the reason lives elsewhere.

The "what" is verified by the compiler and tests and stays in sync automatically. The "why" is invisible and easily lost, and losing it is what causes someone to "fix" a deliberate workaround six months later and reintroduce a bug.

Related: [Readable Code over Clever Code](#readable-code-over-clever-code), [No Magic](#no-magic), [23 Documentation and Decisions](../standards/23-documentation-and-decisions.md) (ADRs).

## Declare Intent, Don't Spell Out Mechanics

Code should read in the vocabulary of the problem, not the vocabulary of the machine. To sum values in a list, call `Sum()`; do not write a loop that initializes an accumulator, iterates, and adds to it. The declarative version states what the code does, while the loop makes every reader reconstruct it from how.

- Prefer built-in declarative operations (`Sum`, `Where`, `Any`, `Max`, `Select`) over hand-rolled loops that re-implement them.
- Extract and name business operations: `CalculateLateFee(booking)` beats the same arithmetic inlined in a handler. Code that reflects the business process is code a domain expert can review.
- A plain `foreach` is still the right tool when you genuinely perform an action per element (send an email to each attendee).
- This is not a license for clever one-liners. If a LINQ chain needs decoding, split it and name the intermediate results (see [Readable Code over Clever Code](#readable-code-over-clever-code)).

A manual loop forces the reader to verify mechanics (accumulator initialization, bounds, mutation) just to discover that it is a sum. A named operation states the intent directly, removes mutable state, and removes branches from the function (see [Keep Code Complexity Low](#keep-code-complexity-low)).

```csharp
// Mechanics: the reader must decode this to learn it's a sum
decimal total = 0;
foreach (var registration in registrations)
{
    total += registration.Fee;
}

// Intent: the code says what the requirement says
var total = registrations.Sum(r => r.Fee);
```

Related: [Readable Code over Clever Code](#readable-code-over-clever-code), [Keep Code Complexity Low](#keep-code-complexity-low), [10 Vertical Slices and CQRS](../standards/10-vertical-slices-and-cqrs.md).

## DRY (Correct Meaning)

DRY, Don't Repeat Yourself, is often misunderstood. It does not mean never writing similar-looking code. The definition is: every piece of knowledge must have a single, authoritative representation in the system.

Two pieces of code that look the same do not necessarily represent the same knowledge. Merging them into a shared abstraction causes problems when they diverge.

- Before abstracting, ask whether the two things represent the same concept or only look similar today.
- Duplication is far cheaper than the wrong abstraction. A wrong abstraction forces every future change to fight the existing structure.
- Do not extract shared code just because it looks similar.

Example: two workflows both validate emails, but only one uses a shared `Email` value object, because its validation rules truly match. The other has different rules and keeps its own validation.

Related: [Reduce Accidental Complexity](#reduce-accidental-complexity), [Readable Code over Clever Code](#readable-code-over-clever-code).

## Don't Prematurely Optimize

Build for correctness and clarity first. Optimize once you have evidence, in the form of metrics, that a bottleneck exists and is worth addressing.

- Measure before you optimize. Profile and identify the actual bottleneck.
- Measure again after changes to confirm they had the intended effect.
- Treat unexplained performance work the same as unexplained feature work: it needs justification.

Most speculative optimizations are wrong about where the real problem is. You pay a real cost in complexity and readability for a change that does not matter. Knuth: premature optimization is the root of all evil.

Instead, profile first. A slow report endpoint should be profiled before the code is changed, not after. Use a tool that fits where the slowness is: dotnet-trace, a database query-analysis tool, or browser DevTools.

Related: [YAGNI](#yagni), [Keep Code Complexity Low](#keep-code-complexity-low).

## Keep Code Complexity Low

Complex code is harder to understand, harder to test, and harder to change safely. The main ways to keep it in check:

- **Keep cyclomatic complexity low.** If a function has too many branches, it is doing too much. Aim for functions that do one clear thing.
- **Keep nesting depth low.** Deeply nested `if` blocks are a sign that logic should be extracted or flattened.
- **Do not pass boolean flags into functions to indicate different flows.** That is a sign the function should be split into two.
- **Prefer composition over inheritance.** Build behavior by combining small, focused functions rather than deep class hierarchies.
- **Model workflows as pipelines of pure functions.** Each step does one thing and returns a new value, which makes the flow easy to follow and test.
- **Parse, don't validate.** Convert inputs into strong types early so validation logic is not repeated throughout the codebase.
- **Keep functions small.** A function should do one thing at one level of abstraction; when it needs a comment to separate its steps, extract them.

Standard 25 sets a complexity limit that the build checks: [25 Code Quality Guards](../standards/25-code-quality-guards.md) (rule 25.9).

Related: [Readable Code over Clever Code](#readable-code-over-clever-code), [11 Domain Modelling and Errors](../standards/11-domain-modelling-and-errors.md), [Modelling principles](modelling.md).

## No Magic

You should be able to follow the code and see exactly what it does. Avoid frameworks and patterns that hide behavior behind runtime reflection, convention scanning, or implicit wiring.

- Prefer explicit code over hidden behavior.
- Favor source generators over runtime magic, since the generated code is readable and debuggable.
- Conventions that are standard for the stack (for example Wolverine handler discovery) are fine when they are inspectable and covered by tests. Hidden behavior that nobody can inspect or test is not.
- If you cannot easily follow the execution path in a code review, treat that as a red flag.

Magic is easy to introduce and hard to debug. When something breaks you need the full execution path, and if half of it happens through reflection or convention-based auto-wiring, finding the cause is slow.

Example: AutoMapper compared with Riok.Mapperly. AutoMapper uses runtime reflection and implicit configuration. Mapperly generates source code at compile time, so you can see exactly what mapping happens and a mistake fails at compile time.

To avoid: heavy runtime reflection that obscures behavior (AutoMapper, complex DI conventions, and similar).

Related: [Readable Code over Clever Code](#readable-code-over-clever-code), [24 AI Agent Tooling](../standards/24-ai-agent-tooling.md).

## Readable Code over Clever Code

If a junior developer cannot understand your "elegant" one-liner, rewrite it. We optimize for maintenance.

Kernighan's Law: debugging is twice as hard as writing the code in the first place. Therefore, if you write the code as cleverly as possible, you are, by definition, not smart enough to debug it.

- Prefer solutions that are obvious over solutions that are impressive.
- Avoid one-liners that hide intent. A few named variables and an `if` statement beat a nested ternary.
- Use explicit naming and control flow.
- Code is read far more often than it is written. Optimize for the reader.

Example: replace a nested ternary with a couple of named boolean variables and a simple `if` chain. It is a few more lines, but anyone can understand it at a glance.

Related: [Keep Code Complexity Low](#keep-code-complexity-low), [No Magic](#no-magic), [Declare Intent, Don't Spell Out Mechanics](#declare-intent-dont-spell-out-mechanics).

## Reduce Accidental Complexity

There are two kinds of complexity: essential (the inherent difficulty of the problem) and accidental (complexity you introduced yourself). Minimize accidental complexity as aggressively as possible.

- Use folders instead of separate projects when there is no real reason to split. Most of the time there is not.
- Keep domain logic separate from infrastructure. Kestrel configuration should not live next to business rules.
- Resist the pull toward microservices for their own sake. A microservice takes hard problems and adds network calls to them. That cost has to be justified, not accepted by default.

Examples of accidental complexity:

- Splitting a monolith into microservices before you know where the boundaries are.
- Creating a shared library for code that is only used in one place.
- Using a complex distributed pattern for a problem that a single database query could solve.

Example: a new feature is implemented as a module in the existing service rather than a new microservice. It ships in the same deployment, shares the same database, and adds no network hops.

Related: [YAGNI](#yagni), [No Magic](#no-magic), [10 Vertical Slices and CQRS](../standards/10-vertical-slices-and-cqrs.md), [Architecture and domain principles](architecture-and-domain.md).

## YAGNI

You Aren't Gonna Need It. Do not build features or abstractions before there is a real need for them.

- Build what is needed now. Defer generalization until you have at least two concrete use cases.
- Resist making things "pluggable" or "extensible" until you know what needs to be extended.
- YAGNI does not mean writing throwaway code. Write clean code for today's requirements and do not speculate about tomorrow's.

Speculative features take time to build and time to maintain. They often make the code more complex without ever being used, and when the real requirement arrives the speculative abstraction is usually wrong anyway.

Example: the team implements a single payment provider now and defers building a "payment provider plugin system" until there is a second provider to integrate.

Related: [Don't Prematurely Optimize](#dont-prematurely-optimize), [Reduce Accidental Complexity](#reduce-accidental-complexity).

## Understand the Problem Before Choosing Architecture

Architectural patterns are answers to specific pressures, not checklist items to adopt because they are fashionable. Before reaching for microservices, event sourcing, CQRS, or a message bus, be able to state what you understand about the problem that justifies the decision.

- Start with the forces: what are the real constraints, failure modes, and business processes at play? Let those drive the pattern, not the other way around.
- Ask "What do we know about this problem that makes this pattern the right answer?" If the honest answer is "it's best practice" or "we might need it later", stop.
- Prefer the simplest thing that fits what you understand today. Add structure once the pressure is real and visible.
- Record the reasoning, not just the choice, in an ADR (see [23 Documentation and Decisions](../standards/23-documentation-and-decisions.md)). A decision you cannot justify from the problem is a decision you cannot revisit safely.

When technical decisions are made before the domain is understood, patterns get selected like items on a shopping list. The result is complexity that solves imaginary problems while the real ones stay unaddressed. Understanding the domain first keeps architecture proportionate to the actual challenge.

Example: instead of opening a new service with "we'll use event sourcing and CQRS", the team first maps the business process (see [Modelling principles](modelling.md)), identifies where consistency and auditability actually matter, and only then decides whether event sourcing earns its cost for that one bounded context.

Related: [Architecture and domain principles](architecture-and-domain.md), [YAGNI](#yagni), [Reduce Accidental Complexity](#reduce-accidental-complexity).

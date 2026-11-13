# Modelling

This page describes the modelling techniques we use for architecture and requirements alignment: the C4 model for architecture, Event Storming for exploring a domain, Event Modelling for specifying a system, and diagram types for business processes and interactions. The techniques are used in that order: explore, specify, then document and keep current.

## C4 Model
Use the C4 model to document system architecture. Created by Simon Brown, C4's four levels (Context, Containers, Components and Code) let you zoom in or out depending on your audience, from a high-level overview for stakeholders down to detailed component diagrams for developers.

Two diagrams are required:

- **System Context**: shows the system's boundaries and how it relates to external systems and users.
- **Container**: shows how the system is composed internally (web app, API, database, message broker and so on).

Keep both diagrams current. Outdated diagrams are misleading and erode trust in the documentation.

Capture:

- integrations with external systems
- databases and storage
- authentication mechanisms
- message brokers where applicable
- key components and their technology stack

For each integration, explain why it exists, not just what it is. "Fetches the logged-in user's personal information" is more useful than "integrates with the central data API".

Deployment diagrams are optional. Add one if the infrastructure is complex enough to warrant it. For detailed interaction design within the system, use [Event Modelling](#event-modelling). Keep the C4 diagrams as editable source in the repo (Structurizr DSL or Excalidraw) with an exported SVG, as set out in [23 Documentation and Decisions](../standards/23-documentation-and-decisions.md) rule 4. Miro is fine for workshop sketches but is not the source of record.

Resources: [C4 Model website](https://c4model.com/), [Structurizr](https://structurizr.com/).

Example workspace for a simple web application, defining both required diagrams:

```structurizr
workspace "Name" "Description" {

    !identifiers hierarchical

    model {
        u = person "User"
        a = softwareSystem "Authentication" "OIDC provider" "external"
        c = softwareSystem "Central Data API" "Shared person and organisation data" "internal"
        p = softwareSystem "Payment" "External payment provider" "external"
        g = softwareSystem "Notifications" "Shared notification service" "internal"
        ss = softwareSystem "Software System" {
            wa = container "Web Application"
            db = container "Database Schema" {
                tags "Database"
            }
        }

        u -> ss.wa "Uses"
        u -> a "Authenticates through"
        u -> p "Completes payment using"
        ss.wa -> a "Reads OIDC config"
        ss.wa -> c "Reads logged in person data"
        ss.wa -> p "Initializes payment"
        ss.wa -> g "Sends notifications through"
        ss.wa -> ss.db "Reads from and writes to"
    }

    views {
        systemContext ss "Diagram1" {
            include *
        }

        container ss "Diagram2" {
            include *
        }

        styles {
            element "Element" {
                color #0773af
                stroke #0773af
                strokeWidth 7
                shape roundedbox
            }
            element "Person" {
                shape person
            }
            element "Database" {
                shape cylinder
            }
            element "Boundary" {
                strokeWidth 5
            }
            element "external" {
                color #888888
                stroke #666666
            }
            element "internal" {
                color #004e48
                stroke #124d47
            }
            relationship "Relationship" {
                thickness 4
            }
        }

    }

    configuration {
        scope softwaresystem
    }

}
```

Related: [23 Documentation and Decisions](../standards/23-documentation-and-decisions.md).

## Event Storming
Event Storming is a collaborative workshop technique, created by Alberto Brandolini, for exploring a business domain together with the people who know it. Domain experts and developers map out a process as a timeline of domain events on sticky notes. It is fast, visual and cheap to throw away and redo.

The core idea is to start with domain events (things that happened, written in past tense, such as "Order Placed" or "Booking Approved") and lay them out in rough chronological order. Then enrich the timeline with commands, actors, external systems and hotspots (points of confusion or disagreement). The disagreements are the point: they surface misunderstandings that would otherwise show up as bugs months later.

### Levels
Event Storming works at three levels of zoom. Pick the one that matches your goal:

- **Big Picture**: map an entire business line end to end with everyone in the room. Good for kicking off a new project or aligning many teams.
- **Process Modelling**: zoom in on one business process and get it precise: events, commands, policies ("whenever X happens, do Y") and read models.
- **Software Design**: detail level for the developers building it. The output maps onto aggregates and bounded contexts.

### Guidance
- Invite the actual domain experts, not their managers. The workshop is worthless without the people who know how the process really works.
- Events first, in past tense. Resist modelling data or UI before the events are on the wall.
- Mark hotspots instead of resolving every dispute in the room. Capture the disagreement and move on.
- Timebox it. A Big Picture session is a half day, not a week.
- Remote sessions work well in Miro. In person, use a paper roll and real sticky notes.

### When to use it
Use Event Storming at the start of a project, when entering an unfamiliar domain, or whenever developers and stakeholders seem to be describing different systems. The output is shared understanding. To turn that understanding into a buildable design, continue with [Event Modelling](#event-modelling).

Resources: [EventStorming website](https://www.eventstorming.com/), [Introducing EventStorming (Alberto Brandolini)](https://leanpub.com/introducing_eventstorming).

Related: [Architecture and domain](architecture-and-domain.md), [11 Domain Modelling and Errors](../standards/11-domain-modelling-and-errors.md), [13 Messaging and Integration](../standards/13-messaging-and-integration.md).

## Event Modelling
Event Modelling, created by Adam Dymitruk, is a technique for describing a system as a blueprint: a single diagram showing how information flows through the system over time. Where [Event Storming](#event-storming) is about discovering a process, Event Modelling is about specifying the system you are going to build.

The blueprint reads left to right as a timeline. Wireframes or UI sketches run along the top, events sit on horizontal stream lanes, and commands and read models connect the two, showing where information enters the system and where it comes back out.

### Building blocks
An event model uses only four kinds of building blocks:

- **Events**: facts that happened, stored on streams ("Order Placed").
- **Commands**: intents to change state ("Place Order").
- **Read models (views)**: the information a user or system reads ("Order Summary").
- **UI / triggers**: the wireframe or system that issues commands and reads views.

These combine into four repeating patterns: state change (UI, command, event), state view (event, read model, UI), translation (external event to internal event) and automation (read model to command, a policy acting on its own).

### Guidance
- Model the complete flow before building. Every piece of information on a screen must be traceable back to an event that put it there. Gaps in the model are requirements questions, cheap to answer now and expensive later.
- Each vertical slice of the model (one command or one view) is an independently implementable unit of work. Estimate and build slice by slice.
- Keep the blueprint current. It doubles as system documentation.
- Create the model in Miro.

### When to use it
Use Event Modelling when designing a new system or a significant new capability, after the domain has been explored (typically with Event Storming). It fits naturally with event-sourced systems, and the blueprint is just as useful for planning conventional ones.

Resources: [Event Modeling website](https://eventmodeling.org/posts/what-is-event-modeling/).

Related: [10 Vertical Slices and CQRS](../standards/10-vertical-slices-and-cqrs.md), [09 Endpoints and API Design](../standards/09-endpoints-and-api-design.md) (task-oriented APIs), [Architecture and domain](architecture-and-domain.md).

## Modelling business processes and interactions
Model the business processes and interactions in a system before building it. Modelling surfaces misunderstandings early, improves communication between developers and stakeholders and makes sure the system solves the right problems.

The main modelling techniques we use:

- **Event Modelling**: designing and visualizing the flow of events and interactions within a system. See [Event Modelling](#event-modelling), and [Event Storming](#event-storming) for the workshop technique used to explore the domain first.
- **State Transition Diagrams**: show the states a system or component can be in, and what moves it between them. Good for lifecycles.
- **Flow Diagrams**: show the flow of data or control through a system.
- **Sequence Diagrams**: show who talks to whom, in what order, as interactions between components or parties over time.
- **Process Flow Diagrams**: show the steps and decision points in a business process.

Pick at least one diagram type to capture the business process, and keep it current. A stale diagram is often worse than none at all.

Event modelling can be done in Miro. Flow diagrams can be created using Mermaid.

Resources: [Event Modeling website](https://eventmodeling.org/posts/what-is-event-modeling/), [Mermaid documentation](https://mermaid-js.github.io/mermaid/#/).

Related: [13 Messaging and Integration](../standards/13-messaging-and-integration.md), [09 Endpoints and API Design](../standards/09-endpoints-and-api-design.md).

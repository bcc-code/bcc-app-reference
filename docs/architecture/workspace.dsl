workspace "Template application" "C4 model of the BCC application template (the Notes sample)" {

    !identifiers hierarchical

    model {
        user = person "Signed-in user" "Keeps personal notes."
        admin = person "Administrator" "Reads note statistics. Signs in with MFA."

        group "BCC Platform" {
            bccSignon = softwareSystem "BCC Signon" "login.bcc.no (Auth0). Signs users in and ends their sessions centrally." "External"
            bccPubSub = softwareSystem "BCC PubSub" "Event bus for integration events. The sample sends none." "External"
        }

        app = softwareSystem "Template application" "Personal notes. The sample an application is made from." {
            frontend = container "Frontend" "Single-page app. index.html comes from the API; the hashed /assets come from a bucket behind Cloud CDN." "Vue 3, TypeScript" "Web Browser"
            api = container "API" "Serves the API under /api and the SPA's index.html. Signs users in as a BFF with server-side sessions." ".NET 11, Wolverine.HTTP, EF Core" {
                notes = component "Notes" "Create, edit and archive a person's notes. A note the caller cannot see answers 404."
                currentUser = component "Current user" "Who is signed in, and their roles."
                adminStats = component "Admin" "Note counts for administrators."
            }
            messages = container "Message topic" "Internal messages between the API's own handlers, pushed back to the API. Locally the durable inbox does this." "Google Pub/Sub" "Queue"
            database = container "Database" "Notes, sessions, Data Protection keys, and Wolverine's outbox, inbox and dead letters." "PostgreSQL" "Database"
        }

        user -> app.frontend "Uses" "HTTPS"
        admin -> app.frontend "Uses" "HTTPS"

        app.frontend -> app.api "Reads and writes data" "HTTPS, JSON"

        app.api -> bccSignon "Signs users in (BFF, session cookie, PKCE and PAR)" "OIDC"
        bccSignon -> app.api "Ends sessions at /auth/backchannel-logout" "HTTPS, signed logout token"
        app.api -> app.database "Reads and writes" "SQL"
        app.api -> app.messages "Publishes internal messages after commit" "Google Pub/Sub"
        app.messages -> app.api "Pushes messages to /internal/pubsub" "HTTPS, Google OIDC"
        app.api -> bccPubSub "Publishes integration events (optional)" "HTTPS, client credentials"

        app.api.notes -> app.database "Stores notes"
        app.api.adminStats -> app.database "Counts notes"

        local = deploymentEnvironment "Local" {
            developer = deploymentNode "Developer machine" "" "Windows, macOS or Linux" {
                aspire = deploymentNode "Aspire AppHost" "aspire run" ".NET Aspire" {
                    apiProcess = deploymentNode "API process" "" ".NET 11" {
                        apiInstance = containerInstance app.api
                    }
                    vite = deploymentNode "Vite dev server" "Proxied by the API (YARP), not browsed directly." "Node.js, Vite" {
                        frontendInstance = containerInstance app.frontend
                    }
                    postgres = deploymentNode "PostgreSQL container" "" "Docker" {
                        databaseInstance = containerInstance app.database
                    }
                    mocks = infrastructureNode "OIDC mock" "api/OidcMock stands in for BCC Signon with test personas. No Pub/Sub locally: internal messages run from the durable inbox, and the AppHost calls the durability pass in a loop." ".NET 11"
                }
            }
        }

        sandbox = deploymentEnvironment "Sandbox" {
            monitoring = deploymentNode "BCC monitoring" "The platform's shared monitoring project (bcc-monitoring)." "Google Cloud Platform" {
                collector = infrastructureNode "OpenTelemetry collector" "Writes traces to Cloud Trace (this project and the central one) and metrics to Managed Prometheus for Grafana. ADR 0026." "Cloud Run"
            }
            gcp = deploymentNode "Google Cloud" "Project from the platform landing zone." "Google Cloud Platform" {
                gateway = infrastructureNode "Platform gateway" "<app>.sandbox.bcc.no. TLS, DNS and routing." "Cloud Load Balancing"
                cdn = infrastructureNode "CDN" "Caches the frontend's hashed assets." "Cloud CDN"

                assetsBucket = deploymentNode "Assets bucket" "Built SPA assets, uploaded additively on deploy." "Cloud Storage" {
                    frontendInstance = containerInstance app.frontend
                }
                scheduler = infrastructureNode "Cloud Scheduler" "Durability pass every minute." "Cloud Scheduler"
                kms = infrastructureNode "Cloud KMS" "Wraps the Data Protection keys." "Cloud KMS"
                pubsub = deploymentNode "Pub/Sub" "<app>-messages-sandbox, push subscription with a dead-letter topic." "Google Pub/Sub" {
                    messagesInstance = containerInstance app.messages
                }
                cloudRun = deploymentNode "Cloud Run" "<app>-api-sandbox, request-based billing, scales to zero." "Cloud Run service" {
                    apiInstance = containerInstance app.api
                }
                cloudSql = deploymentNode "Cloud SQL" "The platform's shared instance, one database per environment, IAM authentication through the Cloud SQL Auth Proxy." "Cloud SQL for PostgreSQL" {
                    databaseInstance = containerInstance app.database
                }
            }

            sandbox.gcp.gateway -> sandbox.gcp.cdn "Routes /assets/*" "HTTPS"
            sandbox.gcp.cdn -> sandbox.gcp.assetsBucket.frontendInstance "Fetches on cache miss" "HTTPS"
            sandbox.gcp.gateway -> sandbox.gcp.cloudRun.apiInstance "Routes everything else: index.html (auth check), /api and /health" "HTTPS"
            sandbox.gcp.scheduler -> sandbox.gcp.cloudRun.apiInstance "Calls /internal/durability/pass" "HTTPS, Google OIDC"
            sandbox.gcp.cloudRun.apiInstance -> sandbox.gcp.kms "Wraps and unwraps Data Protection keys" "HTTPS"
            sandbox.gcp.cloudRun.apiInstance -> sandbox.monitoring.collector "Sends traces and metrics" "OTLP, gRPC"
        }

        preview = deploymentEnvironment "Preview" {
            gcp = deploymentNode "Google Cloud (dev project)" "The landing zone's dev environment, used only for PR previews. One set of services per PR, named ...-pr<N>. ADR 0027." "Google Cloud Platform" {
                cloudRun = deploymentNode "Cloud Run" "<app>-api-pr<N>, scales to zero, one instance, run.app URL." "Cloud Run service" {
                    apiInstance = containerInstance app.api
                }
                mocks = infrastructureNode "OIDC mock" "<app>-oidc-pr<N> signs in the personas in place of BCC Signon." "Cloud Run"
                scheduler = infrastructureNode "Cloud Scheduler" "Durability pass every 30 minutes." "Cloud Scheduler"
                pubsub = deploymentNode "Pub/Sub" "One topic per PR, push subscription with a dead-letter topic." "Google Pub/Sub" {
                    messagesInstance = containerInstance app.messages
                }
                cloudSql = deploymentNode "Cloud SQL" "The platform's shared instance; one schema pr<N> per preview." "Cloud SQL for PostgreSQL" {
                    databaseInstance = containerInstance app.database
                }
            }

            preview.gcp.cloudRun.apiInstance -> preview.gcp.mocks "Signs users in" "HTTPS"
            preview.gcp.scheduler -> preview.gcp.cloudRun.apiInstance "Calls /internal/durability/pass" "HTTPS, Google OIDC"
        }
    }

    views {
        systemContext app "SystemContext" "Who uses the application and which systems it depends on." {
            include *
            autolayout tb
        }

        container app "Containers" "The containers inside the application and what they talk to." {
            include *
            autolayout tb
        }

        component app.api "Components" "The feature slices in the API." {
            include *
            autolayout tb
        }

        deployment app local "LocalDeployment" "Local development with Aspire." {
            include *
            autolayout lr
        }

        deployment app sandbox "SandboxDeployment" "The sandbox environment in GCP." {
            include *
            exclude "sandbox.gcp.assetsBucket.frontendInstance -> sandbox.gcp.cloudRun.apiInstance"
            autolayout lr
        }

        deployment app preview "PreviewDeployment" "A PR preview in the dev project of the landing zone." {
            include *
            autolayout lr
        }

        properties {
            # No generation date in the footer, so an export only changes when the model does.
            "structurizr.metadata" "false"
        }

        styles {
            element "Element" {
                color #369030
                stroke #369030
                strokeWidth 7
                shape roundedbox
            }
            element "Person" {
                shape person
            }
            element "Container" {
                stroke #60aa5a
            }
            element "Component" {
                stroke #a3d9a5
                shape component
            }
            element "Database" {
                shape cylinder
            }
            element "Queue" {
                shape pipe
            }
            element "External" {
                color #505050
                stroke #505050
            }
            element "Web Browser" {
                shape webbrowser
            }
        }
    }
}

#!/bin/sh
# Creates the local Pub/Sub topics and subscriptions in the emulator, as Terraform does in GCP (infra/pubsub.tf):
#   template-messages          the topic the outbox publishes every integration message to
#   template-messages-push     push subscription: the emulator posts each message to $API/internal/pubsub/messages
#   template-dead-letter       where a message goes after 5 failed deliveries
#   template-dead-letter-inspect   pull subscription to read the dead letters with
# Idempotent: 409 (already exists) counts as success. Runs in the curl image, so it is POSIX sh and curl only.
set -eu
base="$PUBSUB/v1/projects/$PROJECT"

until curl -sf "$PUBSUB/" > /dev/null; do sleep 1; done

# Prints nothing; stops the script on any answer other than 200 (created) or 409 (exists).
put() {
  code=$(curl -s -o /dev/null -w '%{http_code}' -X PUT -H 'Content-Type: application/json' -d "$2" "$base/$1")
  case "$code" in 200|409) ;; *) echo "creating $1 failed with HTTP $code"; exit 1 ;; esac
}

put topics/template-messages '{}'
put topics/template-dead-letter '{}'
put subscriptions/template-dead-letter-inspect \
  "{\"topic\":\"projects/$PROJECT/topics/template-dead-letter\",\"ackDeadlineSeconds\":60}"
put subscriptions/template-messages-push \
  "{\"topic\":\"projects/$PROJECT/topics/template-messages\",\"ackDeadlineSeconds\":60,\"pushConfig\":{\"pushEndpoint\":\"$API/internal/pubsub/messages\"},\"deadLetterPolicy\":{\"deadLetterTopic\":\"projects/$PROJECT/topics/template-dead-letter\",\"maxDeliveryAttempts\":5}}"

echo "Pub/Sub topics and push subscription are ready"

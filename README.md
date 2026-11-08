# Vikunja Discord Gateway

Receives Vikunja webhooks at `POST /webhook` and sends a Discord embed for **every event name**, including task, comment, attachment, relation, project, and user events. The message contains the available project, task, actor, and event details. Unknown future event names are forwarded too.

For task updates, the embed shows the task's current labels, assignees, description, due date, and status when Vikunja includes them. Vikunja's `task.updated` event does not identify which field changed, so these values are labelled as current state rather than a change history. Comments and descriptions are converted from HTML to readable text. Task and project links are attached to the embed title.

The gateway uses `data.task.project_id`, `data.project.id`, or `data.project_id` to route a delivery to a configured project. Events without a matching project use the `DEFAULT` Discord webhook. A missing route returns HTTP 422; a failed Discord delivery returns HTTP 502. `GET /health` returns HTTP 200.

## Build

From this directory, run:

```sh
docker build -t vikunja-discord-gateway:local .
```

The Dockerfile builds and publishes the .NET 10 application, then runs it on port 8080 in the ASP.NET runtime image.

## Compose

Keep your existing `vikunja` service. Replace the gateway service with:

```yaml
  vikunja-discord-gateway:
    build:
      context: /path/to/vikunja-discord-gateway
    image: vikunja-discord-gateway:local
    container_name: vikunja-discord-gateway
    restart: always
    environment:
      VIKUNJADISCORDGATEWAY__DRYRUN: "false"
      VIKUNJADISCORDGATEWAY__VIKUNJAURL: "https://fatd.gnetwork.uk/"
      VIKUNJADISCORDGATEWAY__WEBHOOKS__DEFAULT: "${DISCORD_WEBHOOK_URL}"
      VIKUNJADISCORDGATEWAY__PROJECTS__3__NAME: "FA To-Do"
      VIKUNJADISCORDGATEWAY__PROJECTS__3__WEBHOOK: "DEFAULT"
      # Optional: set the same secret in Vikunja's webhook configuration.
      # VIKUNJADISCORDGATEWAY__WEBHOOKSECRET: "${VIKUNJA_WEBHOOK_SECRET}"
```

Set `DISCORD_WEBHOOK_URL` in your Compose `.env` file to your actual Discord webhook URL. Change the build context to the checkout path on the Docker host. Build and start with `docker compose up -d --build vikunja-discord-gateway`.

In Vikunja's project webhook settings, set the target to `http://vikunja-discord-gateway:8080/webhook` and select **all events you want delivered**. The gateway can forward every event it receives, but Vikunja only sends selected events. Your existing `VIKUNJA_OUTGOINGREQUESTS_ALLOWNONROUTABLEIPS: "true"` allows the internal Docker address. For user-level reminder and overdue events, create a separate user webhook with the same target; these use `DEFAULT` when no project is present.

The optional `VIKUNJADISCORDGATEWAY__WEBHOOKSECRET` verifies Vikunja's `X-Vikunja-Signature` using HMAC-SHA256. Set the identical secret on each Vikunja webhook pointing to this gateway. Discord mentions are disabled in outgoing messages.

# Change the API contract

The OpenAPI document `api/BccCode.Template.Api/BccCode.Template.Api.json` is committed and is the contract
([ADR 0005](../adrs/0005-openapi-contract-and-generated-client.md)). No build writes it. After a change to an endpoint's request,
response, route or tag:

```bash
pnpm --dir frontend run generate       # jasper openapi writes the document, then Hey API regenerates frontend/src/client/
git status                             # commit the document and the client together with the backend change
```

`pnpm --dir frontend run openapi-ts` regenerates only the client from the committed document.

- CI fails when the document or the generated client is stale. A test in the integration project compares the served document
  with the committed file.
- The document is byte-stable (sorted, LF line endings). A diff in it means the contract changed.
- `/internal/**` routes are not in the document. The frontend calls the API only through the generated client and the one fetch
  wrapper that handles 401 → login.
- Tags and operation ids come from the feature folder and the endpoint class name; do not set them by hand.

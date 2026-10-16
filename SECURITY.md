# Security policy

This independently maintained BaGetter fork is **not** security-certified for public deployment.

## Reporting

Use GitHub's private vulnerability reporting under the repository's **Security** tab if available. If it is not available, file a **minimal issue requesting a private communication channel, without describing the vulnerability**. Do not publish exploit details, credentials, internal addresses, real package contents, private repository links or sensitive logs.

Once a private channel is established, provide the affected commit, sanitized reproduction, security impact and relevant deployment conditions.

## Deployment

The default configuration is intended for evaluation. Restrict package publishing with validated authentication and authorization, trusted TLS termination, network controls and request limits. Back up and test recovery of **both** the database and package/symbol storage. Optional read-through caching is not a transparent full-protocol NuGet proxy and does not guarantee distributed transaction semantics.

See [operations](docs/OPERATIONS.md) and [mirror limitations](docs/MIRROR.md). No fixed security response or support SLA is promised.

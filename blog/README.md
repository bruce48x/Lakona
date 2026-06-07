# Lakona Docs

Hugo site for Lakona documentation — covers Lakona.Rpc and Lakona.Game.

## Sections

- **RPC** (`content/rpc/`) — strongly typed bidirectional RPC framework docs, reference, and tutorials.
- **Game** (`content/game/`) — actor-based game-session infrastructure docs and tutorials.

Canonical RPC pages that package READMEs and root docs should link to instead
of duplicating long explanations:

- [Getting Started](content/rpc/posts/lakona-rpc-getting-started.md)
- [Design boundary](content/rpc/posts/design-boundary.md)
- [Generated RpcClient](content/rpc/reference/generated-client.md)

## Local Usage

```bash
cd blog
hugo server
```

## Build

```bash
cd blog
hugo
```

## GitHub Pages

The site is deployed from the repository `blog/` directory through GitHub Pages:

- `https://bruce48x.github.io/lakona/`

If the deployment target changes, update `baseURL` in `blog/hugo.toml`.

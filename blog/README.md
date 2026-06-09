# Lakona Docs

Hugo site for Lakona documentation — covers Lakona.Rpc and Lakona.Game.

## Structure

- **Posts** (`content/posts/`) — tutorials, design notes, deployment guides, and architecture articles.
- **Reference** (`content/reference/`) — API reference and generated client documentation.

Canonical pages that package READMEs and root docs should link to instead
of duplicating long explanations:

- [Getting Started](content/posts/lakona-rpc-getting-started.md)
- [Design boundary](content/posts/design-boundary.md)
- [Generated RpcClient](content/reference/generated-client.md)

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

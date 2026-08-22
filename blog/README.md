# Lakona Docs

Hugo site for Lakona.

## Structure

- **Posts** (`content/posts/`) — tutorials, design notes, deployment guides, and architecture articles.

Canonical pages that package READMEs and root docs should link to instead
of duplicating long explanations:

- [Getting Started](content/posts/getting-started.md)
- [Use Lakona Observability](content/posts/observability.md)

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

- `https://bruce48x.github.io/Lakona/`

If the deployment target changes, update `baseURL` in `blog/hugo.toml`.

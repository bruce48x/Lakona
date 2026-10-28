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
npm install
npm run dev
```

The development script uses the pinned `hugo-bin` package and accepts the
standard preview host and port arguments, for example
`npm run dev -- --host 127.0.0.1 --port 7100`.

## Build

```bash
cd blog
npm run build
```

You can also run `hugo server` or `hugo` directly when Hugo is installed.

## GitHub Pages

The site is deployed from the repository `blog/` directory through GitHub Pages:

- `https://bruce48x.github.io/Lakona/`

If the deployment target changes, update `baseURL` in `blog/hugo.toml`.

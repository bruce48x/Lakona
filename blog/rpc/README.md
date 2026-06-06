# Lakona.Rpc Blog

This directory contains a Hugo site for GitHub Pages deployment.

## Documentation sections

The site is organized around stable entry points:

- [Posts](content/posts/)
- [Reference](content/reference/_index.md)

Canonical pages that package READMEs and root docs should link to instead of duplicating long explanations:

- [Getting Started](content/posts/lakona-rpc-getting-started.md)
- [Design boundary](content/posts/design-boundary.md)
- [Generated RpcClient](content/reference/generated-client.md)

## Local usage

```bash
cd docs
hugo server
```

## Build

```bash
cd docs
hugo
```

## GitHub Pages

The repository workflow builds this site from `blog/` and deploys the generated `blog/public/` artifact to GitHub Pages.

The site base URL is configured in `blog/hugo.toml` for the repository project site:

- `https://bruce48x.github.io/Lakona.Rpc/`

If you later move this site to a user or organization site repository, update `baseURL` accordingly.

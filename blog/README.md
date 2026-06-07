# Lakona Docs

Hugo site for Lakona documentation — covers Lakona.Rpc and Lakona.Game.

## Sections

- **RPC** (`content/rpc/`) — strongly typed bidirectional RPC framework docs, reference, and tutorials.
- **Game** (`content/game/`) — actor-based game-session infrastructure docs and tutorials.

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

# Blog Marketing Redesign Spec

**Date:** 2026-06-17
**Status:** Approved

## Goal

Transform `blog/` from a technical documentation site into a marketing-oriented landing page. Beautiful visuals, smooth animations, minimal text.

## Design Decisions

### Visual Direction: Premium/Refined

Deep obsidian background with warm gold accents. Serif typography (Georgia) for headings and body, monospace (SF Mono/Consolas) for code. Inherits and elevates the existing blog's DNA.

**Color Palette:**

| Token | Value | Usage |
|---|---|---|
| Background | `#090806` | Page background |
| Panel | `#14120e` | Card surfaces |
| Ink | `#f6edd8` | Primary text |
| Accent | `#d6a93a` | Gold |
| Accent Light | `#f1d38a` | Bright gold, headings |
| Muted | `#b8ad95` | Secondary text, meta |
| Border | `rgba(214,169,58,0.24)` | Card/panel borders |

**Typography:**
- Headings: Georgia, 700, ranging from 2rem (h1) to 1.2rem (h3)
- Body: Georgia, 400, 1.125rem, line-height 1.6
- Code: SF Mono / Consolas, monospace
- Eyebrow labels: Georgia, 0.7rem, uppercase, letter-spacing 0.15em

**Spacing:** 8px multiples: 4, 8, 12, 16, 24, 32, 48, 64

### Site Structure: Lean Landing Page

Two pages only:
1. **Landing page** (`/`) — five-section scroll
2. **About page** (`/about/`) — simplified

All 20 existing blog posts removed. Documentation links point to GitHub.

### Landing Page Sections

1. **Hero** — large gold heading "C# Networking That Ships", subtitle, dual CTAs (Get Started → GitHub, View on GitHub)
2. **Features** — 4 cards: Typed RPC, Game Server, Multi-Engine, Performance. Each: emoji icon + short description.
3. **Code Preview** — terminal-chrome code window showing a real `[RpcService]` interface. Abstract SVG node graph background.
4. **Guides** — 3 entry-point cards (Quick Start, Deploy, Performance Tuning) linking to GitHub docs.
5. **Footer** — Lakona logo, MIT license, GitHub link, About link.

### Visual Imagery: Mixed Abstract + Code

- Abstract golden SVG node graphs (circles connected by lines, radial gradients) as background/hero elements
- Terminal-chrome code windows with syntax highlighting (GitHub dark palette)
- Feature cards with emoji icons

### Animation: Subtle CSS-Only

| Element | Effect | Method |
|---|---|---|
| Hero SVG nodes | Breathing pulse (continuous) | CSS `@keyframes opacity` |
| Feature cards | Fade-in + slide-up on scroll (staggered) | Intersection Observer → CSS class toggle |
| Code window | Slide-in from left on scroll, cursor blink | CSS transition + `@keyframes blink` |
| Cards/buttons | Hover: gold border glow + scale(1.02) | CSS `:hover` with `transition` |
| Sticky nav | Backdrop blur deepens on scroll | `backdrop-filter` + scroll class |
| Page transition | 200ms fade-in | CSS `transition: opacity` |

- Zero JS animation libraries required
- `prefers-reduced-motion` respected
- `will-change` used sparingly

## Implementation Scope

### Files to Create/Modify

- `blog/hugo.toml` — update title, description
- `blog/layouts/_default/baseof.html` — simplified nav (Logo + GitHub + About), updated head
- `blog/layouts/index.html` — complete rewrite: five-section landing page
- `blog/layouts/_default/single.html` — simplified post layout (for About page)
- `blog/layouts/_default/list.html` — remove or redirect
- `blog/static/css/site.css` — complete redesign with new tokens, animations, sections
- `blog/content/_index.md` — simplified frontmatter
- `blog/content/about.md` — simplified, visual-friendly
- `blog/content/posts/` — DELETE all 20 posts

### Files to Remove

- `blog/content/posts/*.md` — all 20 blog posts
- `blog/layouts/_default/_markup/render-codeblock-mermaid.html` — no longer needed
- `blog/layouts/_default/list.html` — no post list page

### Out of Scope

- Generating actual image assets (uses inline SVG + CSS)
- JavaScript animation libraries
- New Hugo content types
- Analytics, SEO metadata beyond basic description

## Navigation

Two links in sticky header: **GitHub** (external) and **About** (internal). No post list, no API reference.

## Acceptance Criteria

1. `hugo server` in `blog/` renders the landing page with all five sections
2. About page accessible from nav
3. All animations visible but not distracting
4. Zero console errors
5. Responsive: sections stack correctly on mobile (≤700px)
6. No broken links
7. All 20 blog posts removed

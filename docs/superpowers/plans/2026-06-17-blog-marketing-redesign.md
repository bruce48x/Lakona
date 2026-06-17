# Blog Marketing Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transform `blog/` from a technical documentation Hugo site into a two-page marketing landing page with premium gold-on-dark design, abstract SVG+code visuals, and CSS-only subtle animations.

**Architecture:** Hugo static site with two pages (landing `/` and about `/about/`). The landing page is a single scroll with five sections rendered by `layouts/index.html`. All styling in a single `site.css` with CSS custom properties for tokens and CSS keyframe animations. Abstract SVG graphics are inline in templates; code snippets are static HTML in templates. Navigation is minimal: logo + GitHub + About.

**Tech Stack:** Hugo (existing), HTML templates, CSS (custom properties, keyframes, transitions), inline SVG, zero JavaScript libraries.

---

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `blog/hugo.toml` | Modify | Site metadata (title, description) |
| `blog/content/_index.md` | Modify | Landing page frontmatter |
| `blog/content/about.md` | Modify | Simplified about page |
| `blog/content/posts/*.md` | Delete (18 files) | All old blog posts removed |
| `blog/layouts/_default/baseof.html` | Modify | HTML shell: head, nav, footer wrapper |
| `blog/layouts/index.html` | Rewrite | Five-section landing page |
| `blog/layouts/_default/single.html` | Modify | Single page layout (About) |
| `blog/layouts/_default/list.html` | Delete | No longer needed |
| `blog/layouts/_default/_markup/render-codeblock-mermaid.html` | Delete | No longer needed |
| `blog/static/css/site.css` | Rewrite | Complete visual redesign |

---

### Task 1: Update Hugo Configuration

**Files:**
- Modify: `blog/hugo.toml`

- [ ] **Step 1: Update hugo.toml**

Replace the contents of `blog/hugo.toml`:

```toml
baseURL = "https://bruce48x.github.io/lakona/"
languageCode = "en-us"
title = "Lakona"

[params]
description = "C# networking infrastructure for online multiplayer games. Strongly-typed RPC and game server framework for Unity, Godot, and .NET."
repo = "https://github.com/bruce48x/lakona"

[markup]
  [markup.highlight]
    noClasses = false
  [markup.goldmark]
    [markup.goldmark.renderer]
      unsafe = true
```

- [ ] **Step 2: Verify Hugo loads config**

Run: `cd blog && hugo version`
Expected: Prints Hugo version, no errors.

- [ ] **Step 3: Commit**

```bash
git add blog/hugo.toml
git commit -m "chore: update blog title and description for marketing site"
```

---

### Task 2: Delete Old Content and Unused Layouts

**Files:**
- Delete: `blog/content/posts/` (all 18 .md files)
- Delete: `blog/layouts/_default/list.html`
- Delete: `blog/layouts/_default/_markup/render-codeblock-mermaid.html`

- [ ] **Step 1: Delete all blog posts**

```powershell
Remove-Item -Path "blog/content/posts/*.md"
```

- [ ] **Step 2: Delete unused layouts**

```powershell
Remove-Item -Path "blog/layouts/_default/list.html"
Remove-Item -Path "blog/layouts/_default/_markup/render-codeblock-mermaid.html"
```

- [ ] **Step 3: Verify deletions**

```powershell
Get-ChildItem "blog/content/posts/"  # should be empty
Get-ChildItem "blog/layouts/_default/"  # should not contain list.html
Get-ChildItem "blog/layouts/_default/_markup/"  # should not exist or be empty
```

- [ ] **Step 4: Commit**

```bash
git add blog/content/posts/ blog/layouts/
git commit -m "chore: remove all blog posts and unused layouts"
```

---

### Task 3: Rewrite CSS (Complete Redesign)

**Files:**
- Modify: `blog/static/css/site.css`

- [ ] **Step 1: Write the new site.css**

Replace the entire contents of `blog/static/css/site.css`:

```css
/* ==============================================
   Lakona — Marketing Landing Page
   Premium gold-on-dark. Minimal. Code-aware.
   ============================================== */

/* -- Design Tokens ----------------------------------------------- */
:root {
  --bg: #090806;
  --panel: #14120e;
  --ink: #f6edd8;
  --muted: #b8ad95;
  --accent: #d6a93a;
  --accent-light: #f1d38a;
  --border: rgba(214, 169, 58, 0.24);
  --border-subtle: rgba(214, 169, 58, 0.10);
  --shadow: 0 18px 48px rgba(0, 0, 0, 0.32);
  --radius: 18px;
  --radius-sm: 12px;
}

/* -- Reset -------------------------------------------------------- */
*,
*::before,
*::after {
  box-sizing: border-box;
  margin: 0;
  padding: 0;
}

/* -- Base --------------------------------------------------------- */
html {
  scroll-behavior: smooth;
}

body {
  color: var(--ink);
  background:
    radial-gradient(circle at 50% 0%, rgba(214, 169, 58, 0.10), transparent 48rem),
    radial-gradient(circle at 80% 30%, rgba(214, 169, 58, 0.04), transparent 32rem),
    linear-gradient(180deg, #110e08 0%, var(--bg) 100%);
  font-family: Georgia, "Times New Roman", serif;
  line-height: 1.7;
  font-size: 1.125rem;
  -webkit-font-smoothing: antialiased;
}

a {
  color: var(--accent);
  text-decoration: none;
  transition: color 0.2s ease;
}

a:hover {
  color: var(--accent-light);
}

/* -- Layout ------------------------------------------------------- */
.wrap {
  width: min(1040px, calc(100% - 40px));
  margin: 0 auto;
}

/* -- Navigation --------------------------------------------------- */
.site-header {
  position: sticky;
  top: 0;
  z-index: 100;
  backdrop-filter: blur(12px);
  background: rgba(9, 8, 6, 0.78);
  border-bottom: 1px solid var(--border-subtle);
  transition: background 0.3s ease, border-color 0.3s ease;
}

.site-header.scrolled {
  background: rgba(9, 8, 6, 0.92);
  border-bottom-color: var(--border);
}

.site-header .wrap {
  display: flex;
  justify-content: space-between;
  align-items: center;
  min-height: 64px;
  gap: 16px;
}

.brand {
  font-size: 1.25rem;
  font-weight: 700;
  color: var(--accent-light);
  letter-spacing: -0.01em;
  transition: color 0.2s ease;
}

.brand:hover {
  color: var(--ink);
}

.nav {
  display: flex;
  gap: 20px;
  align-items: center;
}

.nav a {
  color: var(--muted);
  font-size: 0.9rem;
  transition: color 0.2s ease;
}

.nav a:hover {
  color: var(--accent-light);
}

/* -- Hero Section ------------------------------------------------- */
.hero {
  text-align: center;
  padding: 80px 24px 72px;
  position: relative;
  overflow: hidden;
}

/* Abstract SVG node graph — background layer */
.hero-graph {
  position: absolute;
  inset: 0;
  pointer-events: none;
}

.hero-graph .node {
  fill: none;
  stroke: var(--accent);
  animation: nodePulse 3s ease-in-out infinite;
}

.hero-graph .node:nth-child(2) { animation-delay: 0.6s; }
.hero-graph .node:nth-child(3) { animation-delay: 1.2s; }
.hero-graph .node:nth-child(4) { animation-delay: 0.3s; }
.hero-graph .node:nth-child(5) { animation-delay: 0.9s; }

.hero-graph .edge {
  stroke: var(--accent);
  opacity: 0.15;
}

.hero-graph .dot {
  fill: var(--accent-light);
  animation: nodePulse 3s ease-in-out infinite;
}

.hero-graph .dot:nth-child(odd) { animation-delay: 0.4s; }
.hero-graph .dot:nth-child(even) { animation-delay: 1.0s; }

.hero .eyebrow {
  color: var(--muted);
  letter-spacing: 0.14em;
  text-transform: uppercase;
  font-size: 0.75rem;
  margin-bottom: 12px;
}

.hero h1 {
  font-size: clamp(2.2rem, 5vw, 3.4rem);
  font-weight: 700;
  color: var(--accent-light);
  line-height: 1.15;
  margin-bottom: 16px;
  letter-spacing: -0.02em;
}

.hero .lead {
  font-size: 1.1rem;
  color: var(--muted);
  max-width: 36rem;
  margin: 0 auto 32px;
  line-height: 1.6;
}

.hero-actions {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
  justify-content: center;
}

/* -- Buttons ------------------------------------------------------ */
.button {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 11px 22px;
  border-radius: 999px;
  font-size: 0.9rem;
  font-weight: 600;
  font-family: inherit;
  cursor: pointer;
  transition: transform 0.2s ease, box-shadow 0.2s ease, border-color 0.2s ease;
  border: 1px solid var(--border);
  color: var(--muted);
  background: rgba(255, 255, 255, 0.03);
}

.button:hover {
  transform: scale(1.03);
  border-color: var(--accent);
  color: var(--ink);
}

.button.primary {
  background: linear-gradient(135deg, var(--accent), var(--accent-light));
  color: #171107;
  border-color: rgba(241, 211, 138, 0.48);
}

.button.primary:hover {
  box-shadow: 0 0 28px rgba(214, 169, 58, 0.35);
  transform: scale(1.03);
}

/* -- Section Headings --------------------------------------------- */
.section-label {
  color: var(--muted);
  letter-spacing: 0.12em;
  text-transform: uppercase;
  font-size: 0.72rem;
  text-align: center;
  margin-bottom: 8px;
}

.section-title {
  font-size: 1.6rem;
  font-weight: 700;
  color: var(--accent-light);
  text-align: center;
  margin-bottom: 36px;
  letter-spacing: -0.01em;
}

/* -- Features Grid ------------------------------------------------ */
.features {
  padding: 72px 0;
}

.features-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 16px;
}

.feature-card {
  background: var(--panel);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius);
  padding: 28px 20px;
  text-align: center;
  box-shadow: var(--shadow);
  transition: transform 0.25s ease, border-color 0.25s ease, box-shadow 0.25s ease;
  opacity: 0;
  transform: translateY(24px);
}

.feature-card.visible {
  opacity: 1;
  transform: translateY(0);
}

.feature-card:hover {
  transform: translateY(-4px);
  border-color: var(--border);
  box-shadow: 0 24px 56px rgba(0, 0, 0, 0.4);
}

.feature-icon {
  font-size: 2rem;
  margin-bottom: 12px;
  display: block;
}

.feature-card h3 {
  font-size: 1rem;
  font-weight: 700;
  color: var(--accent-light);
  margin-bottom: 6px;
}

.feature-card p {
  font-size: 0.85rem;
  color: var(--muted);
  line-height: 1.5;
}

/* -- Code Showcase ------------------------------------------------- */
.code-showcase {
  padding: 72px 0;
}

.code-window {
  background: #050504;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  box-shadow: var(--shadow);
  max-width: 600px;
  margin: 0 auto;
  overflow: hidden;
  opacity: 0;
  transform: translateX(-24px);
  transition: opacity 0.6s ease, transform 0.6s ease;
}

.code-window.visible {
  opacity: 1;
  transform: translateX(0);
}

.code-chrome {
  background: rgba(20, 18, 14, 0.8);
  padding: 10px 14px;
  display: flex;
  gap: 8px;
  align-items: center;
  border-bottom: 1px solid var(--border-subtle);
}

.code-chrome .dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
}

.dot-red  { background: #ff5f56; }
.dot-yel  { background: #ffbd2e; }
.dot-grn  { background: #27c93f; }

.code-chrome .filename {
  margin-left: 8px;
  color: var(--muted);
  font-family: "SFMono-Regular", Consolas, monospace;
  font-size: 0.75rem;
}

.code-body {
  padding: 20px 24px;
  font-family: "SFMono-Regular", Consolas, monospace;
  font-size: 0.85rem;
  line-height: 1.7;
  overflow-x: auto;
}

/* GitHub-dark-inspired syntax colors */
.code-body .c  { color: #8b949e; font-style: italic; }  /* comment */
.code-body .k  { color: #ff7b72; }                        /* keyword */
.code-body .nc { color: #f0883e; font-weight: 700; }       /* type name */
.code-body .nf { color: #d2a8ff; font-weight: 700; }       /* function */
.code-body .s  { color: #a5d6ff; }                        /* string */
.code-body .kt { color: #ff7b72; }                        /* keyword type */
.code-body .na { color: #79c0ff; }                        /* attribute */
.code-body .nl { color: #79c0ff; font-weight: 700; }       /* label */

/* Cursor blink */
.code-cursor {
  display: inline-block;
  width: 2px;
  height: 1em;
  background: var(--accent-light);
  vertical-align: text-bottom;
  margin-left: 2px;
  animation: cursorBlink 0.9s step-end infinite;
}

/* -- Guides Section ----------------------------------------------- */
.guides {
  padding: 72px 0;
}

.guides-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 16px;
}

.guide-card {
  background: var(--panel);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius);
  padding: 28px 20px;
  text-align: center;
  box-shadow: var(--shadow);
  transition: transform 0.25s ease, border-color 0.25s ease, box-shadow 0.25s ease;
  opacity: 0;
  transform: translateY(24px);
}

.guide-card.visible {
  opacity: 1;
  transform: translateY(0);
}

.guide-card:hover {
  transform: translateY(-4px);
  border-color: var(--border);
  box-shadow: 0 24px 56px rgba(0, 0, 0, 0.4);
}

.guide-icon {
  font-size: 2rem;
  margin-bottom: 12px;
  display: block;
}

.guide-card h3 {
  font-size: 1rem;
  font-weight: 700;
  color: var(--accent-light);
  margin-bottom: 6px;
}

.guide-card p {
  font-size: 0.85rem;
  color: var(--muted);
  line-height: 1.5;
}

.guide-card .arrow {
  display: inline-block;
  margin-top: 12px;
  color: var(--accent);
  font-size: 0.85rem;
  font-weight: 600;
  transition: color 0.2s ease;
}

.guide-card:hover .arrow {
  color: var(--accent-light);
}

/* -- Footer ------------------------------------------------------- */
.site-footer {
  border-top: 1px solid var(--border-subtle);
  padding: 48px 0;
  text-align: center;
}

.site-footer .brand {
  font-size: 1.1rem;
  margin-bottom: 8px;
  display: block;
}

.site-footer .muted {
  color: var(--muted);
  font-size: 0.8rem;
  margin-bottom: 16px;
}

.footer-links {
  display: flex;
  gap: 20px;
  justify-content: center;
  font-size: 0.85rem;
}

.footer-links a {
  color: var(--muted);
  transition: color 0.2s ease;
}

.footer-links a:hover {
  color: var(--accent-light);
}

/* -- About Page --------------------------------------------------- */
.article {
  background: var(--panel);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  box-shadow: var(--shadow);
  padding: 40px;
  margin: 40px 0 80px;
}

.article .article-header {
  margin-bottom: 24px;
}

.article .article-header h1 {
  font-size: 2rem;
  font-weight: 700;
  color: var(--accent-light);
  letter-spacing: -0.01em;
}

.article .content {
  color: var(--ink);
  line-height: 1.8;
}

.article .content h2 {
  font-size: 1.3rem;
  font-weight: 700;
  color: var(--accent-light);
  margin-top: 28px;
  margin-bottom: 10px;
}

.article .content p {
  margin-bottom: 12px;
}

.article .content strong {
  color: var(--accent-light);
}

/* -- Keyframe Animations ------------------------------------------ */
@keyframes nodePulse {
  0%, 100% { opacity: 0.25; }
  50%      { opacity: 0.65; }
}

@keyframes cursorBlink {
  0%, 100% { opacity: 1; }
  50%      { opacity: 0; }
}

/* -- Reduced Motion ----------------------------------------------- */
@media (prefers-reduced-motion: reduce) {
  *,
  *::before,
  *::after {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
  }

  html {
    scroll-behavior: auto;
  }

  .feature-card,
  .guide-card,
  .code-window {
    opacity: 1;
    transform: none;
  }
}

/* -- Responsive --------------------------------------------------- */
@media (max-width: 800px) {
  .features-grid {
    grid-template-columns: repeat(2, 1fr);
  }

  .guides-grid {
    grid-template-columns: 1fr;
  }

  .hero {
    padding: 48px 16px 48px;
  }

  .hero h1 {
    font-size: 1.8rem;
  }
}

@media (max-width: 500px) {
  .features-grid {
    grid-template-columns: 1fr;
  }

  .site-header .wrap {
    flex-direction: column;
    align-items: flex-start;
    padding: 12px 0;
  }

  .article {
    padding: 24px;
  }
}
```

- [ ] **Step 2: Commit**

```bash
git add blog/static/css/site.css
git commit -m "feat: redesign site.css with premium marketing theme"
```

---

### Task 4: Rewrite Base Template (Navigation + Shell)

**Files:**
- Modify: `blog/layouts/_default/baseof.html`

- [ ] **Step 1: Write the new baseof.html**

Replace the entire contents of `blog/layouts/_default/baseof.html`:

```html
<!doctype html>
<html lang="{{ .Site.Language.Lang | default "en-us" }}">
  <head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>{{ if .Title }}{{ .Title }} | {{ end }}{{ .Site.Title }}</title>
    <meta name="description" content="{{ with .Params.description }}{{ . }}{{ else }}{{ .Site.Params.description }}{{ end }}">
    <link rel="stylesheet" href="{{ "css/site.css" | relURL }}">
  </head>
  <body>
    <header class="site-header" id="site-header">
      <div class="wrap">
        <a class="brand" href="{{ .Site.Home.RelPermalink }}">{{ .Site.Title }}</a>
        <nav class="nav">
          <a href="{{ "about/" | relURL }}">About</a>
          <a href="{{ .Site.Params.repo }}" target="_blank" rel="noopener">GitHub</a>
        </nav>
      </div>
    </header>
    <main>{{ block "main" . }}{{ end }}</main>
    <footer class="site-footer">
      <div class="wrap">
        <span class="brand">{{ .Site.Title }}</span>
        <p class="muted">Open source &middot; MIT License</p>
        <div class="footer-links">
          <a href="{{ .Site.Params.repo }}" target="_blank" rel="noopener">GitHub</a>
          <a href="{{ "about/" | relURL }}">About</a>
        </div>
      </div>
    </footer>
    <script>
      // Sticky header scroll effect
      const header = document.getElementById('site-header');
      window.addEventListener('scroll', () => {
        header.classList.toggle('scrolled', window.scrollY > 20);
      });

      // Scroll-triggered fade-in animations
      const observer = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
          if (entry.isIntersecting) {
            entry.target.classList.add('visible');
          }
        });
      }, { threshold: 0.15 });

      document.querySelectorAll('.feature-card, .guide-card, .code-window').forEach(el => {
        observer.observe(el);
      });
    </script>
  </body>
</html>
```

- [ ] **Step 2: Commit**

```bash
git add blog/layouts/_default/baseof.html
git commit -m "feat: simplify base template with minimal nav and scroll animations"
```

---

### Task 5: Rewrite Landing Page Template

**Files:**
- Modify: `blog/layouts/index.html`

- [ ] **Step 1: Write the new index.html**

Replace the entire contents of `blog/layouts/index.html`:

```html
{{ define "main" }}

<!-- Hero -->
<section class="hero">
  <svg class="hero-graph" viewBox="0 0 800 400" preserveAspectRatio="xMidYMid slice" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
    <circle class="node" cx="120" cy="180" r="70" stroke-width="0.6"/>
    <circle class="node" cx="400" cy="100" r="90" stroke-width="0.6"/>
    <circle class="node" cx="680" cy="200" r="60" stroke-width="0.6"/>
    <circle class="node" cx="250" cy="300" r="50" stroke-width="0.5"/>
    <circle class="node" cx="550" cy="310" r="75" stroke-width="0.5"/>
    <line class="edge" x1="120" y1="180" x2="400" y2="100" stroke-width="0.5"/>
    <line class="edge" x1="400" y1="100" x2="680" y2="200" stroke-width="0.5"/>
    <line class="edge" x1="120" y1="180" x2="250" y2="300" stroke-width="0.5"/>
    <line class="edge" x1="400" y1="100" x2="550" y2="310" stroke-width="0.5"/>
    <line class="edge" x1="680" y1="200" x2="550" y2="310" stroke-width="0.5"/>
    <line class="edge" x1="250" y1="300" x2="550" y2="310" stroke-width="0.5"/>
    <circle class="dot" cx="120" cy="180" r="4"/>
    <circle class="dot" cx="400" cy="100" r="5"/>
    <circle class="dot" cx="680" cy="200" r="3.5"/>
    <circle class="dot" cx="250" cy="300" r="3"/>
    <circle class="dot" cx="550" cy="310" r="4"/>
  </svg>

  <div style="position:relative;z-index:1">
    <p class="eyebrow">Lakona</p>
    <h1>C# Networking<br>That Ships</h1>
    <p class="lead">{{ .Site.Params.description }}</p>
    <div class="hero-actions">
      <a class="button primary" href="{{ .Site.Params.repo }}">Get Started</a>
      <a class="button" href="{{ .Site.Params.repo }}" target="_blank" rel="noopener">View on GitHub</a>
    </div>
  </div>
</section>

<!-- Features -->
<section class="features">
  <div class="wrap">
    <p class="section-label">Why Lakona</p>
    <h2 class="section-title">Built for multiplayer games</h2>
    <div class="features-grid">
      <div class="feature-card">
        <span class="feature-icon">📡</span>
        <h3>Typed RPC</h3>
        <p>Share C# contracts between server and client. Bidirectional communication over one connection.</p>
      </div>
      <div class="feature-card">
        <span class="feature-icon">🎮</span>
        <h3>Game Server</h3>
        <p>Actor-based framework with hot-reloadable game logic, session management, and reliable push.</p>
      </div>
      <div class="feature-card">
        <span class="feature-icon">🔌</span>
        <h3>Multi-Engine</h3>
        <p>First-class support for Unity, Godot, and standalone .NET. One codebase, every platform.</p>
      </div>
      <div class="feature-card">
        <span class="feature-icon">⚡</span>
        <h3>Performance</h3>
        <p>TCP, WebSocket, and KCP transports. Zero-allocation serialization. Built for production load.</p>
      </div>
    </div>
  </div>
</section>

<!-- Code Showcase -->
<section class="code-showcase">
  <div class="wrap">
    <p class="section-label">Show, Don't Tell</p>
    <h2 class="section-title">Define once. Use everywhere.</h2>
    <div class="code-window">
      <div class="code-chrome">
        <span class="dot dot-red"></span>
        <span class="dot dot-yel"></span>
        <span class="dot dot-grn"></span>
        <span class="filename">IGameService.cs</span>
      </div>
      <div class="code-body">
        <span class="c">// Shared contract — server and client both reference this</span><br>
        <span class="na">[RpcService]</span><br>
        <span class="k">public interface</span> <span class="nc">IGameService</span><br>
        {<br>
        &nbsp;&nbsp;&nbsp;&nbsp;<span class="kt">Task</span>&lt;<span class="nc">LoginResult</span>&gt; <span class="nf">Login</span>(<span class="kt">string</span> token);<br>
        <br>
        &nbsp;&nbsp;&nbsp;&nbsp;<span class="kt">Task</span>&lt;<span class="nc">PlayerData</span>&gt; <span class="nf">GetPlayerData</span>();<br>
        <br>
        &nbsp;&nbsp;&nbsp;&nbsp;<span class="kt">Task</span> <span class="nf">SendChatMessage</span>(<span class="nc">ChatMsg</span> msg);<br>
        }<span class="code-cursor"></span>
      </div>
    </div>
  </div>
</section>

<!-- Guides -->
<section class="guides">
  <div class="wrap">
    <p class="section-label">Documentation</p>
    <h2 class="section-title">Start building in minutes</h2>
    <div class="guides-grid">
      <a class="guide-card" href="{{ .Site.Params.repo }}#readme" target="_blank" rel="noopener">
        <span class="guide-icon">📖</span>
        <h3>Quick Start</h3>
        <p>Scaffold a project and go from zero to running in under five minutes.</p>
        <span class="arrow">Read the guide →</span>
      </a>
      <a class="guide-card" href="{{ .Site.Params.repo }}" target="_blank" rel="noopener">
        <span class="guide-icon">🚀</span>
        <h3>Deploy to Linux</h3>
        <p>Production-ready deployment guide for multi-machine game server clusters.</p>
        <span class="arrow">Read the guide →</span>
      </a>
      <a class="guide-card" href="{{ .Site.Params.repo }}" target="_blank" rel="noopener">
        <span class="guide-icon">⚡</span>
        <h3>Performance Tuning</h3>
        <p>Transport selection, serialization options, and throughput optimization.</p>
        <span class="arrow">Read the guide →</span>
      </a>
    </div>
  </div>
</section>

{{ end }}
```

- [ ] **Step 2: Commit**

```bash
git add blog/layouts/index.html
git commit -m "feat: rewrite landing page with five marketing sections"
```

---

### Task 6: Simplify Single Page Template + Update About Page

**Files:**
- Modify: `blog/layouts/_default/single.html`
- Modify: `blog/content/about.md`
- Modify: `blog/content/_index.md`

- [ ] **Step 1: Rewrite single.html**

Replace the entire contents of `blog/layouts/_default/single.html`:

```html
{{ define "main" }}
  <div class="wrap">
    <article class="article">
      <header class="article-header">
        <h1>{{ .Title }}</h1>
      </header>
      <div class="content">
        {{ .Content }}
      </div>
    </article>
  </div>
{{ end }}
```

- [ ] **Step 2: Simplify about.md**

Replace the entire contents of `blog/content/about.md`:

```markdown
---
title: About Lakona
description: Lakona is open-source networking infrastructure for online multiplayer games.
date: 2026-05-07T11:20:00+08:00
---

## What is Lakona?

Lakona provides open-source networking infrastructure for online multiplayer games.

- **Lakona.Rpc** — a strongly typed bidirectional RPC framework for Unity, Godot, and .NET. Define your API once as a shared C# interface, and let source generators create the networking glue.
- **Lakona.Game** — an actor-based C# game server framework built on Lakona.Rpc. Features hot-reloadable game logic, session management, reliable push, and cluster routing.

## Design Philosophy

- **Shared contracts are the source of truth.** Server and client compile the same interfaces.
- **Zero magic.** Generated code is deterministic, readable, and IL2CPP-friendly.
- **Transports are pluggable.** TCP, WebSocket, KCP — swap without changing your game logic.
- **Production-ready.** Built for real games with real deadlines.
```

- [ ] **Step 3: Simplify _index.md**

Replace the entire contents of `blog/content/_index.md`:

```markdown
---
title: Lakona
description: C# networking infrastructure for online multiplayer games.
---
```

- [ ] **Step 4: Commit**

```bash
git add blog/layouts/_default/single.html blog/content/about.md blog/content/_index.md
git commit -m "feat: simplify single template, about page, and home frontmatter"
```

---

### Task 7: Build and Verify

- [ ] **Step 1: Build the Hugo site**

```powershell
cd blog
hugo
```

Expected: Site builds without errors. Output goes to `blog/public/`.

- [ ] **Step 2: Verify output structure**

```powershell
Get-ChildItem blog/public/ -Recurse -Name | Sort-Object
```

Expected: Contains `index.html`, `about/index.html`, `css/site.css`. No `posts/` directory.

- [ ] **Step 3: Verify no broken internal links**

```powershell
Select-String -Path "blog/public/**/*.html" -Pattern 'href="(?!http)[^"]*"' | ForEach-Object { $_.Matches.Value }
```

Expected: Internal links should be `/about/` and `/`. No references to `/posts/` or deleted pages.

- [ ] **Step 4: Start dev server and spot-check**

```powershell
cd blog
hugo server --noHTTPCache
```

Open `http://localhost:1313` and verify:
- Landing page renders all five sections
- Abstract SVG node graph visible in hero
- "About" nav link works
- "GitHub" nav link opens GitHub in new tab
- CTA buttons link to GitHub
- Scroll down triggers card fade-in animations
- About page renders cleanly
- No console errors
- Mobile responsive (resize to ≤500px)

- [ ] **Step 5: Commit any final tweaks**

```bash
git add -A
git commit -m "chore: final verification tweaks"
```

---

### Task 8: Clean Up Visual Companion Server

- [ ] **Step 1: Stop the visual companion server**

```bash
bash C:\Users\bruce\.agents\skills\brainstorming\scripts\stop-server.sh C:\Users\bruce\Documents\GitHub\lakona\.superpowers\brainstorm\1822-1781709459
```

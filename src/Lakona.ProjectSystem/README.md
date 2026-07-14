# Lakona.ProjectSystem

`Lakona.ProjectSystem` is the shared project-tooling engine used by
`lakona-tool` and Lakona Hub.

It owns Lakona project inspection and creation, including request validation,
generation planning, template rendering, transactional writes, and optional
Git initialization. Applications should call `LakonaProjectCreator` instead of
reimplementing project-generation behavior.

Most users should install Lakona Hub or `Lakona.Tool` rather than referencing
this package directly.

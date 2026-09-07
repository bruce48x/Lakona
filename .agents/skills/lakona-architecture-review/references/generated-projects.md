# 3. Generated-Project Experience

Treat generated projects as the strictest consumer test.

1. Read the current project-tooling authorities and discover the supported
   starter families and defaults.
2. Generate representative current default projects under the repository
   root's `.tmp/` review directory.
   Cover every supported client family when local tools permit it.
3. Inspect direct and transitive package dependencies, version declarations,
   generated project references, build assets, default files, and concepts
   exposed to users.
4. Simulate a package upgrade far enough to count the files, versions, and
   coordinated changes a user must make.
5. Build or run focused scaffold validation when feasible. Request required
   restore or external-tool permission instead of silently skipping it.
6. Record unavailable tools or blocked validation as coverage limitations.

Look specifically for dependency fan-out, independently versioned assets with
one owner, redundant packages, leaked implementation packages, multiple version
authorities, and starter concepts that do not earn their user-facing cost.

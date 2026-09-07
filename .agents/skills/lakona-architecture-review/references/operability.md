# 7. Operability And Diagnosability

Check whether maintainers can detect and localize failures without attaching a
debugger:

- readiness must reflect partial startup, stopping, unhealthy dependencies, and
  lost framework state truthfully
- errors must identify the owner, lifecycle phase, and cause without leaking
  payloads, request values, or user data
- metrics, traces, and events must distinguish network, serialization, queue,
  dispatch, application, and recovery delay
- metric tags must remain low-cardinality
- control and diagnostics paths must remain isolated from the measured data path
- diagnostics cost must not create material allocation, contention, or bandwidth
  regressions

Prefer a small diagnostic interface with high leverage over many counters and
logs that still fail to identify the responsible module.

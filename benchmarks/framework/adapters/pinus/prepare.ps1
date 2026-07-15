$ErrorActionPreference = "Stop"

& npm ci --ignore-scripts
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& npm run build
exit $LASTEXITCODE

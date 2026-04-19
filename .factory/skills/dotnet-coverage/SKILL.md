---
name: dotnet-coverage
description: Measure .NET test coverage with dotnet-coverage, optionally translate reports with dotnet-reportgenerator-globaltool, and correlate results with SonarQube or SonarCloud issues.
user-invocable: true
disable-model-invocation: false
---

# .NET Coverage

## Goal

Generate reproducible .NET coverage reports, convert them when needed, and check related SonarQube/SonarCloud findings.

## Tooling

- Prefer `dotnet-coverage` for collection.
- Use `dotnet-reportgenerator-globaltool` when the user needs HTML summaries or a different report format.

## Verify tool availability

- `dotnet-coverage --version`
- `reportgenerator -version`

If ReportGenerator is missing, use one of:

- `dotnet tool install --global dotnet-reportgenerator-globaltool --version 5.5.4`
- `dotnet tool install --tool-path .\\.tools dotnet-reportgenerator-globaltool --version 5.5.4`
- `dotnet new tool-manifest`
- `dotnet tool install --local dotnet-reportgenerator-globaltool --version 5.5.4`

## Coverage collection workflow

1. Find the repo's build and test entry points.
2. Check for coverage settings such as:
   - `coverage.settings.xml`
   - `coverlet.*`
   - `SonarQube.Analysis.xml`
3. Collect coverage with `dotnet-coverage`.
4. If the user needs another format or a browsable report, translate it with ReportGenerator.

## Preferred commands

### XML output for SonarQube / SonarCloud

```powershell
dotnet-coverage collect dotnet test ".\AdaskoTheBeAsT.Interop.Threading.slnx" --no-build -s ".\coverage.settings.xml" -f xml -o ".\coverage.xml"
```

### Cobertura output

```powershell
dotnet-coverage collect dotnet test ".\AdaskoTheBeAsT.Interop.Threading.slnx" --no-build -s ".\coverage.settings.xml" -f cobertura -o ".\coverage.cobertura.xml"
```

### Translate reports with ReportGenerator

```powershell
reportgenerator -reports:".\coverage.xml" -targetdir:".\coverage-report" -reporttypes:"HtmlSummary;Cobertura"
```

If installed with `--tool-path`:

```powershell
".\.tools\reportgenerator.exe" -reports:".\coverage.xml" -targetdir:".\coverage-report" -reporttypes:"HtmlSummary;Cobertura"
```

## SonarQube / SonarCloud issue check

1. Discover the project key from:
   - `.vscode/settings.json`
   - `.github/workflows/*.yml`
   - `SonarQube.Analysis.xml`
2. Fetch issue data from the server API.
3. Summarize:
   - quality gate
   - coverage
   - open issues
   - hotspots
   - top files and rules
4. Remind the user that cloud/server results stay stale until a fresh analysis runs.

### SonarCloud endpoints

- `https://sonarcloud.io/api/qualitygates/project_status?projectKey=<key>`
- `https://sonarcloud.io/api/measures/component?component=<key>&metricKeys=alert_status,coverage,bugs,vulnerabilities,code_smells,violations,new_coverage,new_bugs,new_vulnerabilities,new_code_smells`
- `https://sonarcloud.io/api/issues/search?componentKeys=<key>&resolved=false&ps=100&facets=severities,types`
- `https://sonarcloud.io/api/hotspots/search?projectKey=<key>&status=TO_REVIEW&ps=100`

### SonarQube endpoints

Use the same paths against the user's SonarQube base URL:

- `<sonarqube-base-url>/api/qualitygates/project_status?projectKey=<key>`
- `<sonarqube-base-url>/api/measures/component?component=<key>&metricKeys=alert_status,coverage,bugs,vulnerabilities,code_smells,violations,new_coverage,new_bugs,new_vulnerabilities,new_code_smells`
- `<sonarqube-base-url>/api/issues/search?componentKeys=<key>&resolved=false&ps=100&facets=severities,types`
- `<sonarqube-base-url>/api/hotspots/search?projectKey=<key>&status=TO_REVIEW&ps=100`

## Guardrails

- Never print tokens or auth headers.
- Never claim SonarQube/SonarCloud is fixed before re-analysis.
- Keep generated coverage artifacts out of tracked files unless the user explicitly asks to commit them.

## Response format

Return:

- commands used
- output file paths
- coverage summary
- translation step used, if any
- relevant SonarQube/SonarCloud findings
- whether re-analysis is still required

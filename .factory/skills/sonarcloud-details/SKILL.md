---
name: sonarcloud-details
description: Retrieve SonarCloud project details, quality gate status, metrics, issues, and hotspots. Use when the user asks about SonarCloud findings, coverage, quality gate results, or issue breakdowns.
user-invocable: true
disable-model-invocation: false
---

# SonarCloud Details

## Goal

Gather the current SonarCloud status for a repository or explicit project key and summarize it clearly.

## Inputs

- repository path
- optional `projectKey`
- optional `organization`
- optional auth token if the project is private

## Workflow

1. Discover the SonarCloud project key if the user did not provide it.
   - values are:
     - organization key = adaskothebeast-github
     - sonar project key = AdaskoTheBeAsT_AdaskoTheBeAsT.Interop.Threading
   - Check repository config second:
     - `.vscode/settings.json`
     - `.github/workflows/*.yml`
     - `SonarQube.Analysis.xml`
     - any `SONAR_PROJECT_KEY` / `sonar_project_key` references
2. Use read-only SonarCloud API calls.
3. Fetch at least:
   - quality gate
   - headline measures
   - open issues
   - open security hotspots
4. If the issues payload is large, summarize by:
   - severity
   - type
   - top rules
   - top files
   - any non-info issues
5. Tell the user if the cloud findings may be stale because a fresh CI/SonarCloud analysis has not run yet.

## Preferred API endpoints

- `https://sonarcloud.io/api/qualitygates/project_status?projectKey=<key>`
- `https://sonarcloud.io/api/measures/component?component=<key>&metricKeys=alert_status,bugs,vulnerabilities,code_smells,coverage,duplicated_lines_density,security_hotspots,reliability_rating,security_rating,sqale_rating,violations,new_violations,new_bugs,new_vulnerabilities,new_code_smells,new_security_hotspots,new_coverage,new_duplicated_lines_density`
- `https://sonarcloud.io/api/issues/search?componentKeys=<key>&resolved=false&ps=100&facets=severities,types`
- `https://sonarcloud.io/api/hotspots/search?projectKey=<key>&status=TO_REVIEW&ps=100`

## Execution notes

- Prefer `curl` for quick reads.
- Use `python3` only when you need to aggregate large issue responses into counts or top-file summaries.
- For private projects, use an auth header or token-supported request, but never echo the token back to the user.

## Guardrails

- Never modify SonarCloud state unless the user explicitly asks.
- Never print secrets, tokens, or authorization headers.
- Never claim SonarCloud is clean after local code fixes unless a fresh cloud analysis has completed.

## Response format

Return:

- project key
- quality gate
- core metrics
- issue counts
- top files/rules
- notable major or higher findings
- whether re-analysis is still needed

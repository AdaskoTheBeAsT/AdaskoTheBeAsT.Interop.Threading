---
name: dotnet-coverage-python-inspector
description: Inspect dotnet-coverage XML reports with Python to answer exact file, function, and line-level coverage questions.
user-invocable: true
disable-model-invocation: false
---

# .NET Coverage Python Inspector

## Goal

Use Python to read `dotnet-coverage` XML directly and answer questions such as:

- which files still have uncovered lines
- which lines are `no`, `partial`, or `yes`
- which function or range is responsible for a line being uncovered
- whether a given report is stale relative to the current source tree

## When to use

- The user wants exact line-level details from `coverage.xml`, `coverage-local.xml`, `coverage-before.xml`, or `coverage-after.xml`
- Droid needs machine-readable coverage details that are easier to reason about than HTML
- The user asks whether specific lines are covered
- The user wants before/after comparison at the file or line level

## Inputs

- path to the coverage XML report
- optional file path filter
- optional line number or line range filter
- optional second coverage report for comparison

## Verify prerequisites

- `python3 --version`
- the report path exists
- source files referenced by the report exist locally

## Core approach

1. Parse the report with `xml.etree.ElementTree`
2. Build a per-module map of `source_id -> source_file path`
3. Walk each `function` and `range`
4. Aggregate line states with precedence `no < partial < yes`
5. Keep function owners for non-fully-covered lines
6. Read source text only for the lines that need to be reported

## Coverage semantics

- `yes` = covered
- `partial` = partially covered
- `no` = uncovered

If a line appears in multiple ranges, report the final aggregated status and keep the relevant function names for diagnostics.

## Preferred Python helper

Use this reusable loader and adapt it per task:

```python
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

STATUS_ORDER = {"no": 0, "partial": 1, "yes": 2}
STATUS_NAME = {0: "no", 1: "partial", 2: "yes"}


def load_coverage(report_path: Path):
    root = ET.parse(report_path).getroot()
    line_status = defaultdict(dict)
    owners = defaultdict(lambda: defaultdict(set))

    for module in root.findall(".//module"):
        source_map = {
            source_file.attrib["id"]: source_file.attrib["path"]
            for source_file in module.findall("./source_files/source_file")
        }

        for function in module.findall("./functions/function"):
            function_name = function.attrib.get("name", "<unknown>")

            for range_node in function.findall("./ranges/range"):
                source_path = source_map.get(range_node.attrib["source_id"])
                if not source_path:
                    continue

                state = STATUS_ORDER.get(range_node.attrib.get("covered", "no"), 0)
                start_line = int(range_node.attrib["start_line"])
                end_line = int(range_node.attrib["end_line"])

                for line_number in range(start_line, end_line + 1):
                    previous = line_status[source_path].get(line_number, -1)
                    if state > previous:
                        line_status[source_path][line_number] = state

                    if state < STATUS_ORDER["yes"]:
                        owners[source_path][line_number].add(function_name)

    return line_status, owners
```

## Common usage patterns

### List uncovered lines by file

```python
report = Path(r"D:\GitHub\repo\coverage.xml")
line_status, owners = load_coverage(report)

for path in sorted(line_status):
    uncovered = [
        line_number
        for line_number, state in sorted(line_status[path].items())
        if state == STATUS_ORDER["no"]
    ]
    if not uncovered:
        continue

    source_lines = Path(path).read_text(encoding="utf-8").splitlines()
    print(path)
    print(f"Uncovered lines: {len(uncovered)}")

    for line_number in uncovered:
        source = source_lines[line_number - 1].rstrip() if line_number - 1 < len(source_lines) else ""
        functions = ", ".join(sorted(owners[path][line_number]))
        print(f"  {line_number}: {source} [{functions}]")

    print()
```

### Check how specific lines are covered

```python
report = Path(r"D:\GitHub\repo\coverage.xml")
targets = {
    r"D:\GitHub\repo\src\MyProject\ExecutionWorker.cs": [53, 54, 90, 91],
}

line_status, owners = load_coverage(report)

for path, line_numbers in targets.items():
    source_lines = Path(path).read_text(encoding="utf-8").splitlines()

    for line_number in line_numbers:
        state_value = line_status.get(path, {}).get(line_number)
        state = STATUS_NAME.get(state_value, "missing")
        source = source_lines[line_number - 1].rstrip() if line_number - 1 < len(source_lines) else ""
        functions = ", ".join(sorted(owners.get(path, {}).get(line_number, set()))) or "-"
        print(f"{path}:{line_number} => {state} | {source} | {functions}")
```

### Compare two reports

Load both reports with the same helper, compute uncovered-line sets per file, and report:

- lines fixed in the newer report
- newly uncovered lines
- files that reached full coverage

## Execution style

- Prefer `python3 -c` for short one-off queries
- For longer analysis, compose a temporary Python script only if needed
- Keep temporary analysis files out of tracked source unless the user explicitly asks to persist them

## Guardrails

- Treat report inspection as read-only work
- Do not claim SonarCloud or SonarQube is fixed until a fresh server analysis runs
- If source paths in the report no longer exist, clearly say the report may be stale
- Return only the relevant lines instead of dumping whole files
- Call out partial coverage separately from fully uncovered lines

## Response format

Return:

- report path analyzed
- module or assembly names
- files inspected
- uncovered lines
- partial lines
- specific requested line statuses
- any stale-report or path-mismatch warning

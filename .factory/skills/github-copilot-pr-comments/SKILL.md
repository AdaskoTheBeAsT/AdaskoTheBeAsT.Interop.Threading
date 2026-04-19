---
name: github-copilot-pr-comments
description: Fetch, triage, and track GitHub Copilot inline review comments on a pull request, including resolved / unresolved state and file-by-file grouping. Use when the user asks about "copilot comments", "copilot PR review", "unresolved copilot feedback", or any similar question about Copilot's automated PR reviewer.
user-invocable: true
disable-model-invocation: false
---

# GitHub Copilot PR Comments

## Goal

Retrieve every Copilot-authored review comment (inline code comments + review-body summaries) on a given pull request, attach the source line text, and report which threads are still unresolved. The skill is read-only.

## When to use

- The user asks "check Copilot comments / review / feedback on PR #N"
- The user asks which Copilot findings are still open / unresolved
- The user wants the exact file + line + comment body for each Copilot suggestion
- The user wants a before/after comparison after a new Copilot re-review

## Inputs

- repository path or explicit `<owner>/<repo>` (auto-detect from `git remote get-url origin`)
- pull-request number (required; detect the current branch's open PR if not supplied)
- optional `GITHUB_TOKEN` environment variable (strongly recommended — unauthenticated IP quota is 60/hour and GraphQL needs a token)

## Tooling

- `curl` for the REST calls
- `python3` for JSON aggregation and source-line fetching
- optional: `gh` CLI if already installed (skip otherwise — do not require it)

## Verify prerequisites

- `git remote get-url origin` → extracts `<owner>/<repo>`
- `python3 --version`
- `curl --version`

## Copilot identity

The inline PR reviewer identifies as one of these logins (seen across tenants over time):

- `copilot-pull-request-reviewer[bot]`
- `github-copilot[bot]`
- any login where `user.login` contains the substring `copilot` (case-insensitive)

Always filter on the login containing `copilot` rather than hard-coding a single string.

## REST endpoints (anonymous IP quota: 60/hour)

- List PRs for current branch:
  `GET /repos/{owner}/{repo}/pulls?head={owner}:{branch}&state=open`
- Inline code comments on the PR (what Copilot posts):
  `GET /repos/{owner}/{repo}/pulls/{number}/comments?per_page=100`
- PR-level reviews (review bodies + review state):
  `GET /repos/{owner}/{repo}/pulls/{number}/reviews?per_page=100`
- Check-runs (to correlate with the Copilot check):
  `GET /repos/{owner}/{repo}/check-suites/{check_suite_id}/check-runs`

## GraphQL endpoint (required for `isResolved`)

Resolution state is **not** exposed by the REST comments endpoint — only GraphQL's `reviewThreads.isResolved` field returns it. Query:

```graphql
{
  repository(owner:"<owner>", name:"<repo>") {
    pullRequest(number:<number>) {
      reviewThreads(first:100) {
        nodes {
          id
          isResolved
          isOutdated
          path
          line
          comments(first:20) {
            nodes {
              databaseId
              author { login }
              body
              outdated
              createdAt
            }
          }
        }
      }
    }
  }
}
```

POST the query to `https://api.github.com/graphql` with header `Authorization: Bearer <token>`. Without a token GraphQL returns HTTP 401 — the REST endpoint is the only anonymous fallback (but it loses `isResolved`).

## Workflow

1. **Resolve repo + PR number.**
   - Repo: `git remote get-url origin` → strip `.git` / `git@github.com:` prefix.
   - PR: if the user did not supply a number, query
     `GET /repos/{owner}/{repo}/pulls?head={owner}:{branch}` for the current branch.
2. **Fetch inline comments (REST).** Save the JSON locally (e.g. `.factory/copilot-comments.json`) so a second invocation does not consume another API call.
3. **Filter to Copilot authors.** Case-insensitive match on `user.login` containing `copilot`.
4. **Fetch PR-level reviews (REST).** Same filter; a review with non-empty `body` is the "summary" block Copilot posts. `state == "COMMENTED"` / `"CHANGES_REQUESTED"` is informational.
5. **Fetch resolution state via GraphQL** (when a token is available). Match `comments.nodes[].databaseId` to the REST comment `id` to build a `comment_id → isResolved` map.
6. **Attach source lines.** For each unresolved comment, open the local file at `path` and read the line at `line` (fall back to `original_line` when the comment is outdated).
7. **Report.** Group by status:
   - `Unresolved` (top of report)
   - `Resolved` (collapsed summary — count only)
   - `Outdated` (collapsed summary)
     Within each group, sort by `path`, then by `line`, and include the full comment body.

## Preferred helper: REST + GraphQL aggregation

```python
import json, os, urllib.request, urllib.error, subprocess
from pathlib import Path

OWNER, REPO, PR = "AdaskoTheBeAsT", "AdaskoTheBeAsT.Interop.Threading", 1
TOKEN = os.environ.get("GITHUB_TOKEN", "")
HEADERS = {"Accept": "application/vnd.github+json"}
if TOKEN:
    HEADERS["Authorization"] = f"Bearer {TOKEN}"


def get_json(url):
    req = urllib.request.Request(url, headers=HEADERS)
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read())


def is_copilot(login):
    return login and "copilot" in login.lower()


comments = get_json(
    f"https://api.github.com/repos/{OWNER}/{REPO}/pulls/{PR}/comments?per_page=100"
)
copilot_inline = [c for c in comments if is_copilot(c.get("user", {}).get("login"))]

reviews = get_json(
    f"https://api.github.com/repos/{OWNER}/{REPO}/pulls/{PR}/reviews?per_page=100"
)
copilot_reviews = [
    r for r in reviews
    if is_copilot(r.get("user", {}).get("login")) and (r.get("body") or "").strip()
]

# GraphQL resolution map (requires token)
resolved = {}
if TOKEN:
    query = {
        "query": f'''
        {{
          repository(owner:"{OWNER}", name:"{REPO}") {{
            pullRequest(number:{PR}) {{
              reviewThreads(first:100) {{
                nodes {{
                  isResolved isOutdated path line
                  comments(first:20) {{
                    nodes {{ databaseId author {{ login }} }}
                  }}
                }}
              }}
            }}
          }}
        }}
        '''
    }
    req = urllib.request.Request(
        "https://api.github.com/graphql",
        data=json.dumps(query).encode(),
        headers={**HEADERS, "Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=30) as resp:
        threads = (json.loads(resp.read())
                   .get("data", {})
                   .get("repository", {})
                   .get("pullRequest", {})
                   .get("reviewThreads", {})
                   .get("nodes", []))
    for t in threads:
        for c in t.get("comments", {}).get("nodes", []):
            if is_copilot(c.get("author", {}).get("login", "")):
                resolved[c["databaseId"]] = {
                    "isResolved": t["isResolved"],
                    "isOutdated": t["isOutdated"],
                }

# Report
unresolved, done, outdated = [], [], []
for c in copilot_inline:
    state = resolved.get(c["id"], {})
    if state.get("isResolved"):
        done.append(c)
    elif state.get("isOutdated"):
        outdated.append(c)
    else:
        unresolved.append(c)

print(f"Copilot review on PR #{PR}")
print(f"  inline comments: {len(copilot_inline)}  "
      f"unresolved: {len(unresolved)}  resolved: {len(done)}  outdated: {len(outdated)}")

for c in unresolved:
    src_line = ""
    path = Path(c["path"])
    if path.exists():
        line_no = c.get("line") or c.get("original_line")
        if line_no:
            try:
                src = path.read_text(encoding="utf-8-sig").splitlines()
                src_line = src[line_no - 1].rstrip() if 0 < line_no <= len(src) else ""
            except OSError:
                pass
    print(f"\n--- UNRESOLVED #{c['id']}  {c['path']}:{c.get('line') or c.get('original_line')}")
    if src_line:
        print(f"    > {src_line}")
    print(f"    {c['body'].strip()[:600]}")
```

## Common usage patterns

### "Check Copilot comments on the current branch's PR"

1. `git remote get-url origin` → owner/repo.
2. `git rev-parse --abbrev-ref HEAD` → branch.
3. `GET .../pulls?head={owner}:{branch}&state=open` → PR number.
4. Run the helper above.

### "Only show unresolved comments"

Filter the REST `copilot_inline` list against the GraphQL `resolved` map where `isResolved == False`. When no token is available, surface a warning that resolution state is not visible and the list may include already-resolved items.

### "Did Copilot re-review after my last push?"

Fetch `pulls/{number}/reviews` and inspect `submitted_at` for Copilot-authored reviews. Re-reviews appear as additional review records with newer timestamps; also re-fetch `pulls/{number}/comments` — new inline comments show `created_at` after your last commit's `committer.date`.

### "Before / after diff"

Persist the comment list as `copilot-comments-before.json` before the push and as `copilot-comments-after.json` after re-review; diff on `id` + `body` to list _new_, _unchanged_, and _addressed_ (missing from after) comments.

## Cache directory

Persist API responses under `.factory/` (gitignored by convention) so repeated invocations do not exhaust the IP-level quota:

- `.factory/copilot-comments.json` — REST inline comments
- `.factory/copilot-reviews.json` — REST PR-level reviews
- `.factory/copilot-threads.json` — GraphQL thread snapshot

## Rate-limit handling

- Anonymous IP quota: 60 requests/hour. Four PR reads (comments + reviews + runs + check-runs) + a GraphQL query ≈ 5 requests; budget accordingly.
- Detect rate-limit error by checking HTTP 403 with `rate limit exceeded` in the body — report clearly to the user and ask for a `GITHUB_TOKEN` env var.
- When running inside GitHub Actions, `${{ secrets.GITHUB_TOKEN }}` (mapped to `GITHUB_TOKEN`) already has 5,000 requests/hour for the repo.

## Guardrails

- Never print the `GITHUB_TOKEN` or echo any header containing `Authorization:` back to the user.
- Never edit or resolve comments — this skill is read-only. The GitHub UI ("Resolve conversation" button) or a follow-up `gh` / `curl` PATCH is the user's job.
- When resolution state is unknown (no token), always label the report "resolution state unavailable" so the user does not assume every listed comment is still open.
- Respect `isOutdated` — outdated comments usually map to code that has since been rewritten or removed, and surfacing them as "open work" is noise.

## Response format

Return:

- repo + PR number
- author identity used for filtering (e.g. `copilot-pull-request-reviewer[bot]`)
- total Copilot inline comments seen
- **unresolved**: full list with file, line, source excerpt, comment body
- **resolved**: collapsed count
- **outdated**: collapsed count
- any new comments since the user's last push (when the skill is invoked after a re-review)
- caveat if resolution state could not be fetched (no token, 401/403 on GraphQL, rate limit, etc.)
